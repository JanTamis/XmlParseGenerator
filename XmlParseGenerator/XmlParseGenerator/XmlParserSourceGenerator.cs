using System;
using System.Linq;
using Microsoft.CodeAnalysis;
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
			.OrderBy(o => o)
			.Select(s => $"using {s};\n");

		var attributes = type.Members
			.Where(w => w.Attribute.AttributeType == AttributeType.Attribute)
			.Select(s => $$"""
				{{s.Attribute.Name}}="{value.{{s.Name}}}"
				""")
			.ToList();
		
		var xml = new IndentedStringBuilder(new string('\t', 2));
		xml.Indent();
		
		xml.AppendLineWithoutIndent("<?xml version=\"1.0\" encoding=\"utf-8\"?>");

		if (attributes.Any())
		{
			xml.AppendLine($"<{type.RootName ?? type.TypeName} {String.Join(" ", attributes)} xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
		}
		else
		{
			xml.AppendLine($"<{type.RootName ?? type.TypeName} xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
		}
		
		xml.Indent();

		// xml.SetIndent(4);
		
		foreach (var member in type.Members)
		{
			if (member.Attribute.AttributeType == AttributeType.Element)
			{
				xml.AppendLine($"<{member.Attribute.Name}>{{value.{member.Name}}}</{member.Attribute.Name}>");
			}
		}

		xml.Unindent();
		xml.AppendIndent($"</{type.RootName ?? type.TypeName}>");
		
		context.AddSource($"{type.TypeName}Serializer.g.cs", $$""""
			using System.IO;
			using System.Threading.Tasks;
			{{String.Concat(namespaces)}}
			public static class {{type.TypeName}}Serializer
			{
				/// <summary>
				/// Serializes the given object to a string.
				/// </summary>
				/// <param name="value">The object to serialize.</param>
				/// <returns>A string representation of the serialized object.</returns>
				public static string Serialize({{type.TypeName}} value)
				{
					return $"""
						{{xml}}
						""";
				}

				/// <summary>
				/// Serializes the given object and writes it to the provided stream.
				/// </summary>
				/// <param name="stream">The stream to which the serialized object will be written.</param>
				/// <param name="value">The object to serialize.</param>
				public static void Serialize(Stream stream, {{type.TypeName}} value)
				{
					using var writer = new StreamWriter(stream);
					writer.Write(Serialize(value));
				}

				/// <summary>
				/// Serializes the given object and writes it asynchronously to the provided stream.
				/// </summary>
				/// <param name="stream">The stream to which the serialized object will be written.</param>
				/// <param name="value">The object to serialize.</param>
				public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value)
				{
					using var writer = new StreamWriter(stream);
					await writer.WriteAsync(Serialize(value));
				}

				/// <summary>
				/// Serializes the given object and writes it to the provided TextWriter.
				/// </summary>
				/// <param name="writer">The TextWriter to which the serialized object will be written.</param>
				/// <param name="value">The object to serialize.</param>
				public static void Serialize(TextWriter writer, {{type.TypeName}} value)
				{
					writer.Write(Serialize(value));
				}

				/// <summary>
				/// Serializes the given object and writes it asynchronously to the provided TextWriter.
				/// </summary>
				/// <param name="writer">The TextWriter to which the serialized object will be written.</param>
				/// <param name="value">The object to serialize.</param>
				public static async Task SerializeAsync(TextWriter writer, {{type.TypeName}} value)
				{
					await writer.WriteAsync(Serialize(value));
				}
			}
			"""");
	}
}