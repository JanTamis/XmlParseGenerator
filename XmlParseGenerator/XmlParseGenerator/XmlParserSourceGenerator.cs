using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using XmlParseGenerator.Enumerable;
using XmlParseGenerator.Models;

namespace XmlParseGenerator;

[Generator]
public class XmlParserSourceGenerator : IIncrementalGenerator
{
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var temp = context.SyntaxProvider.ForAttributeWithMetadataName("System.SerializableAttribute", (node, token) => true, (syntaxContext, token) =>
		{
			var namedType = syntaxContext.TargetSymbol as INamedTypeSymbol;
			var result = new ItemModel
			{
				TypeName = namedType.Name,
				RootName = namedType.GetAttributes()
					.Where(w => w.AttributeClass?.ToDisplayString() == "System.Xml.Serialization.XmlRootAttribute")
					.Select(s => s.ConstructorArguments[0].Value?.ToString())
					.FirstOrDefault(),
				RootNamespace = namedType.ContainingNamespace.ToDisplayString(),
			};

			result.Namespaces.Add(namedType.ContainingNamespace.ToDisplayString());

			foreach (var member in namedType.GetMembers())
			{
				MemberModel? memberModel = null;

				switch (member)
				{
					case IPropertySymbol { CanBeReferencedByName: true, IsStatic: false, IsIndexer: false } property:
						result.Namespaces.Add(property.Type.ContainingNamespace.ToDisplayString());

						memberModel = new MemberModel
						{
							Name = property.Name,
							Type = property.Type.Name,
							SpecialType = property.Type.SpecialType,
							IsClass = property.Type.IsReferenceType,
							MemberType = MemberType.Property,
						};
						break;
					case IFieldSymbol { CanBeReferencedByName: true, IsStatic: false } field:
						result.Namespaces.Add(field.Type.ContainingNamespace.ToDisplayString());

						memberModel = new MemberModel
						{
							Name = field.Name,
							Type = field.Type.Name,
							SpecialType = field.Type.SpecialType,
							IsClass = field.Type.IsReferenceType,
							MemberType = MemberType.Field,
						};
						break;
				}

				if (memberModel is not null)
				{
					var attributes = member.GetAttributes();

					foreach (var attribute in attributes)
					{
						var fullName = attribute.AttributeClass?.ToDisplayString();

						if (fullName is "System.Xml.Serialization.XmlElementAttribute")
						{
							memberModel.Attribute = new AttributeModel()
							{
								Name = attribute.NamedArguments
									.Where(w => w.Key == "ElementName")
									.Select(s => s.Value.Value?.ToString())
									.DefaultIfEmpty(attribute.ConstructorArguments[0].Value?.ToString())
									.FirstOrDefault(),
								AttributeType = AttributeType.Element
							};
						}

						if (fullName is "System.Xml.Serialization.XmlAttributeAttribute")
						{
							memberModel.Attribute = new AttributeModel()
							{
								Name = attribute.NamedArguments
									.Where(w => w.Key == "attributeName")
									.Select(s => s.Value.Value?.ToString())
									.DefaultIfEmpty(attribute.ConstructorArguments[0].Value?.ToString())
									.FirstOrDefault(),
								AttributeType = AttributeType.Attribute
							};
						}
					}

					result.Members.Add(memberModel);
				}
			}

			return result;
		});

		context.RegisterSourceOutput(temp, Generate);
	}

	private void Generate(SourceProductionContext context, ItemModel? type)
	{
		var namespaces = type.Namespaces
			.Where(w => w != "<global namespace>" && w != type.RootNamespace)
			.OrderBy(o => o)
			.Select(s => $"using {s};\n");

		var attributes = type.Members
			.Where(w => w.Attribute.AttributeType == AttributeType.Attribute)
			.Select(s => $$"""
				{{s.Attribute.Name}}="{value.{{s.Name}}}"
				""")
			.ToList();

		var code = $$""""
			using System.IO;
			using System.Text;
			using System.Globalization;
			using System.Threading.Tasks;
			using System.Xml;
			{{String.Concat(namespaces)}}
			namespace {{type.RootNamespace}};

			public static class {{type.TypeName}}Serializer
			{
				public static async Task<string> SerializeAsync({{type.TypeName}} value)
				{
					return await SerializeAsync(value, GetDefaultSettings());
				}
				
				public static async Task<string> SerializeAsync({{type.TypeName}} value, XmlWriterSettings settings)
				{
					settings.Async = true;
				
					var builder = new StringBuilder();
					
					await SerializeAsync(new StringWriter(builder, CultureInfo.InvariantCulture), value, settings);
				
					return builder.ToString();
				}

				public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value)
				{
					await SerializeAsync(textWriter, value, GetDefaultSettings());
				}
				
				public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings settings)
				{
					settings.Async = true;
				
					using var writer = XmlWriter.Create(textWriter, settings);
				
					await writer.WriteStartDocumentAsync();
					await Serialize{{type.TypeName}}Async(writer, value);
					await writer.WriteEndDocumentAsync();
				
					await writer.FlushAsync();
				}
			
				public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value)
				{
					await SerializeAsync(stream, value, GetDefaultSettings());
				}

				public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value, XmlWriterSettings settings)
				{
					settings.Async = true;

					using var writer = XmlWriter.Create(stream, settings);
			
					await writer.WriteStartDocumentAsync();
					await Serialize{{type.TypeName}}Async(writer, value);
					await writer.WriteEndDocumentAsync();
			
					await writer.FlushAsync();
				}

				private static XmlWriterSettings GetDefaultSettings()
				{
					return new XmlWriterSettings
					{
						Indent = true,
						Async = true,
					};
				}

				{{CreateSerializeForType(type)}}
			}
			"""";

		context.AddSource($"{type.TypeName}Serializer.g.cs", code);
	}

	private string CreateSerializeForType(ItemModel? type)
	{
		var builder = new IndentedStringBuilder("\t");

		var members = type.Members.ToLookup(g => g.Attribute.AttributeType);

		builder.AppendLineWithoutIndent($"private static async Task Serialize{type.TypeName}Async(XmlWriter writer, {type.TypeName} item)");
		builder.AppendLine("{");
		builder.Indent();

		builder.AppendLine($"await writer.WriteStartElementAsync(null, \"{type.RootName ?? type.TypeName}\", null);");
		builder.AppendLine();

		foreach (var attribute in members[AttributeType.Attribute])
		{
			if (attribute.IsClass)
			{
				builder.AppendLine($"await writer.WriteAttributeStringAsync(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}?.ToString());");
			}
			else
			{
				builder.AppendLine($"await writer.WriteAttributeStringAsync(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}.ToString());");
			}
		}

		if (members[AttributeType.Attribute].Any())
		{
			builder.AppendLine();
		}		

		foreach (var attribute in members[AttributeType.Element])
		{
			if (attribute.IsClass)
			{
				builder.AppendLine($"await writer.WriteElementStringAsync(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}?.ToString());");
			}
			else
			{
				builder.AppendLine($"await writer.WriteElementStringAsync(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}.ToString());");
			}
		}

		if (members[AttributeType.Element].Any())
		{
			builder.AppendLine();
		}

		builder.AppendLine($"await writer.WriteEndElementAsync();");

		builder.Unindent();
		builder.AppendIndent("}");

		return builder.ToString();
	}
}