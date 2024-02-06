using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
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
			var set = new Dictionary<string, ItemModel>();

			return FillItemModel(namedType, set);
		});

		context.RegisterSourceOutput(temp, Generate);
	}

	private void Generate(SourceProductionContext context, ItemModel? type)
	{
		var namespaces = type.Namespaces
			.Where(w => w != "<global namespace>" && w != type.RootNamespace)
			.OrderBy(o => o)
			.Select(s => $"using {s};\n");

		var serializerTypes = new Dictionary<string, string>();
		var deserializerTypes = new Dictionary<string, string>();
		// var builderText = ParseTypeToBuilder(type, 1, "builder", "AppendLiteral", "AppendFormatted", out var holeCount, out var literalCount);
		// var writerText = ParseTypeToBuilder(type, 1, "textWriter", "Write", "Write", out var writerHoleCount, out var writerLiteralCount);

		CreateSerializeForType(serializerTypes, type);
		CreateDeserializeForType(deserializerTypes, type);

		var code = $$""""
			using System.IO;
			using System.Text;
			using System.Globalization;
			using System.Threading.Tasks;
			using System.Runtime.CompilerServices;
			using System.Xml;
			{{String.Concat(namespaces)}}
			namespace {{type.RootNamespace}};

			public static class {{type.TypeName}}Serializer
			{
				public static string Serialize({{type.TypeName}} value)
				{
					return Serialize(value, null);
				}
				
				public static string Serialize({{type.TypeName}} value, XmlWriterSettings? settings)
				{
					using var builder = new StringWriter();
					
					Serialize(builder, value, settings);
				
					return builder.ToString();
				}
				
				public static void Serialize(TextWriter textWriter, {{type.TypeName}} value)
				{
					Serialize(textWriter, value, null);
				}
				
				public static void Serialize(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings? settings)
				{
					using var writer = XmlWriter.Create(textWriter, settings);
				
					writer.WriteStartDocument();
					Serialize{{type.TypeName}}(writer, value);
					writer.WriteEndDocument();
				
					writer.Flush();
				}
				
				public static void Serialize(Stream stream, {{type.TypeName}} value)
				{
					using var writer = new StreamWriter(stream);
					Serialize(writer, value);
				}
				
				public static void Serialize(Stream stream, {{type.TypeName}} value, XmlWriterSettings? settings)
				{
					using var writer = XmlWriter.Create(stream, settings);
				
					writer.WriteStartDocument();
					Serialize{{type.TypeName}}(writer, value);
					writer.WriteEndDocument();
				
					writer.Flush();
				}
			
				public static async Task<string> SerializeAsync({{type.TypeName}} value)
				{
					return await SerializeAsync(value, GetDefaultSettings());
				}
				
				public static async Task<string> SerializeAsync({{type.TypeName}} value, XmlWriterSettings? settings)
				{
					settings.Async = true;
				
					using var builder = new StringWriter();
					
					await SerializeAsync(builder, value, settings);
				
					return builder.ToString();
				}
			
				public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value)
				{
					await SerializeAsync(textWriter, value, GetDefaultSettings());
				}
				
				public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings? settings)
				{
					settings.Async = true;
				
					await using var writer = XmlWriter.Create(textWriter, settings);
				
					await writer.WriteStartDocumentAsync();
					await Serialize{{type.TypeName}}Async(writer, value);
					await writer.WriteEndDocumentAsync();
				
					await writer.FlushAsync();
				}
			
				public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value)
				{
					await SerializeAsync(stream, value, GetDefaultSettings());
				}
			
				public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value, XmlWriterSettings? settings)
				{
					settings.Async = true;
			
					await using var writer = XmlWriter.Create(stream, settings);
			
					await writer.WriteStartDocumentAsync();
					await Serialize{{type.TypeName}}Async(writer, value);
					await writer.WriteEndDocumentAsync();
			
					await writer.FlushAsync();
				}
				
				public static {{type.TypeName}} Deserialize(string value)
				{
					var xmlReader = XmlReader.Create(new StringReader(value), new()
					{
					    IgnoreComments = true,
					    IgnoreWhitespace = true,
					});
					                              
					while (xmlReader.Read())
					{
				    if (xmlReader.NodeType != XmlNodeType.Element || xmlReader.Depth != 0)
				    {
							continue;
				    }
				    
				    if (xmlReader.Name == "{{type.RootName ?? type.TypeName}}")
				    {
				      return Deserialize{{type.TypeName}}(xmlReader.ReadSubtree(), 1);
				    }
					}
					
					return new {{type.TypeName}}();
				}
			
				private static XmlWriterSettings GetDefaultSettings()
				{
					return new XmlWriterSettings
					{
						Indent = true,
					};
				}
			
				{{String.Join("\n\n\t", serializerTypes.Values)}}
				
				{{String.Join("\n\n\t", deserializerTypes.Values)}}
			}
			"""";

		context.AddSource($"{type.TypeName}Serializer.g.cs", code);
	}

	private void CreateSerializeForType(Dictionary<string, string> result, ItemModel? type)
	{
		if (type is null || result.ContainsKey(type.TypeName) || !IsValidType(type.SpecialType))
		{
			return;
		}

		result.Add(type.TypeName, CreateSerializeForType(type, false));
		result.Add($"{type.TypeName}Async", CreateSerializeForType(type, true));

		foreach (var member in type?.Members ?? System.Linq.Enumerable.Empty<MemberModel>())
		{
			if (!result.ContainsKey(member.Type.TypeName))
			{
				CreateSerializeForType(result, member?.Type);
			}
		}
	}

	private void CreateDeserializeForType(Dictionary<string, string> result, ItemModel? type)
	{
		if (type is null || result.ContainsKey(type.TypeName) || IsValidType(type.SpecialType))
		{
			return;
		}

		result.Add(type.TypeName, CreateDeserializeForType(type, false));
		result.Add($"{type.TypeName}Async", CreateDeserializeForType(type, true));

		foreach (var member in type?.Members ?? System.Linq.Enumerable.Empty<MemberModel>())
		{
			if (!result.ContainsKey(member.Type.TypeName))
			{
				CreateDeserializeForType(result, member?.Type);
			}
		}
	}

	private string CreateSerializeForType(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync ? "async Task" : "void";
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");
		var members = type.Members.ToLookup(g => g.Attribute?.AttributeType);

		builder.AppendLineWithoutIndent($"private static {async} Serialize{type.TypeName}{asyncSuffix}(XmlWriter writer, {type.TypeName} item)");
		builder.AppendLine("{");
		builder.Indent();

		builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{type.RootName ?? type.TypeName}\", null);");
		builder.AppendLine();

		foreach (var attribute in members[AttributeType.Attribute])
		{
			if (type.IsClass)
			{
				builder.AppendLine($"{asyncKeyword}writer.WriteAttributeString{asyncSuffix}(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}?.ToString());");
			}
			else
			{
				builder.AppendLine($"{asyncKeyword}writer.WriteAttributeString{asyncSuffix}(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}.ToString());");
			}
		}

		if (members[AttributeType.Attribute].Any())
		{
			builder.AppendLine();
		}

		foreach (var element in members[AttributeType.Element])
		{
			if (IsValidType(element.Type.SpecialType))
			{
				if (element.Type.IsClass)
				{
					builder.AppendLine();
					builder.AppendLine($"if (item.{element.Name} is not null)");
					builder.AppendLine("{");
					builder.Indent();

					builder.AppendLine($"{asyncKeyword}Serialize{element.Type.TypeName}{asyncSuffix}(writer, item.{element.Name});");

					builder.Unindent();
					builder.AppendLine("}");
				}
				else
				{
					builder.AppendLine($"{asyncKeyword}Serialize{element.Type.TypeName}{asyncSuffix}(writer, item.{element.Name});");
				}
			}
			else
			{
				builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{element.Attribute.Name ?? element.Name}\", null);");

				if (element.Type.SpecialType == SpecialType.System_String)
				{
					builder.AppendLine($"{asyncKeyword}writer.WriteString{asyncSuffix}(item.{element.Name});");
				}
				else
				{
					builder.AppendLine($"writer.WriteValue(item.{element.Name});");
				}

				builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");
				builder.AppendLine();
			}
		}

		builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");

		builder.Unindent();
		builder.AppendIndent("}");

		return builder.ToString();
	}

	private string CreateDeserializeForType(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync ? $"async Task<{type.TypeName}>" : type.TypeName;
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");
		var members = type.Members.ToLookup(g => g.Attribute?.AttributeType);

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}{asyncSuffix}(XmlReader reader, int depth)");
		builder.AppendLine("{");

		using (_ = builder.IndentScope())
		{
			builder.AppendLine($"var result = new {type.TypeName}();");
			builder.AppendLine();
			builder.AppendLine($"while ({asyncKeyword}reader.Read{asyncSuffix}())");
			builder.AppendLine("{");

			using (_ = builder.IndentScope())
			{
				builder.AppendLine("if (reader.NodeType != XmlNodeType.Element || reader.Depth != depth)");
				builder.AppendLine("{");
				using (_ = builder.IndentScope())
				{
					builder.AppendLine("continue;");
				}

				builder.AppendLine("}");
				builder.AppendLine();

				builder.AppendLine("switch (reader.Name)");
				builder.AppendLine("{");
				using (_ = builder.IndentScope())
				{
					foreach (var element in members[AttributeType.Element])
					{
						builder.AppendLine($"case \"{element.Attribute.Name}\":");
						using (_ = builder.IndentScope())
						{
							if (!IsValidType(element.Type.SpecialType))
							{
								builder.AppendLine($"{asyncKeyword}reader.Read{asyncSuffix}();");

								if (element.Type.SpecialType == SpecialType.System_String)
								{
									builder.AppendLine($"result.{element.Name} = reader.Value;");
								}
								else
								{
									builder.AppendLine($"result.{element.Name} = {element.Type.TypeName}.Parse(reader.Value);");
								}
							}
							else
							{
								builder.AppendLine($"result.{element.Name} = Deserialize{element.Type.TypeName}{asyncSuffix}(reader.ReadSubtree(), depth + 1);");
							}


							builder.AppendLine("break;");
						}
					}
				}

				builder.AppendLine("}");
			}

			builder.AppendLine("}");
			builder.AppendLine();
			builder.AppendLine("return result;");
		}

		builder.AppendIndent("}");

		return builder.ToString();
	}

	private ItemModel? FillItemModel(ITypeSymbol? namedType, Dictionary<string, ItemModel> types)
	{
		if (namedType is null )
		{
			return null;
		}

		if (types.TryGetValue($"{namedType.ContainingNamespace.ToDisplayString()}.{namedType.Name}", out var result))
		{
			return result;
		}

		result = new ItemModel
		{
			TypeName = namedType.Name,
			RootName = namedType.GetAttributes()
				.Where(w => w.AttributeClass?.ToDisplayString() == "System.Xml.Serialization.XmlRootAttribute")
				.Select(s => s.ConstructorArguments[0].Value?.ToString())
				.FirstOrDefault(),
			RootNamespace = namedType.ContainingNamespace.ToDisplayString(),
			IsClass = namedType.IsReferenceType,
			SpecialType = namedType.SpecialType,
		};

		types.Add($"{result.RootNamespace}.{result.TypeName}", result);

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
						Type = FillItemModel(property.Type, types),
						MemberType = MemberType.Property,
					};
					break;
				case IFieldSymbol { CanBeReferencedByName: true, IsStatic: false, } field:
					result.Namespaces.Add(field.Type.ContainingNamespace.ToDisplayString());

					memberModel = new MemberModel
					{
						Name = field.Name,
						Type = FillItemModel(field.Type, types),
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

					memberModel.Attribute = fullName switch
					{
						"System.Xml.Serialization.XmlElementAttribute" => new AttributeModel
						{
							Name = attribute.NamedArguments
								.Where(w => w.Key == "ElementName")
								.Select(s => s.Value.Value?.ToString())
								.DefaultIfEmpty(attribute.ConstructorArguments[0].Value?.ToString())
								.FirstOrDefault(),
							AttributeType = AttributeType.Element
						},
						"System.Xml.Serialization.XmlAttributeAttribute" => new AttributeModel
						{
							Name = attribute.NamedArguments
								.Where(w => w.Key == "attributeName")
								.Select(s => s.Value.Value?.ToString())
								.DefaultIfEmpty(attribute.ConstructorArguments[0].Value?.ToString())
								.FirstOrDefault(),
							AttributeType = AttributeType.Attribute
						},
						_ => null,
					};
				}

				if (memberModel.Attribute is null)
				{
					memberModel.Attribute = new AttributeModel
					{
						Name = memberModel.Name,
						AttributeType = AttributeType.Element
					};
				}

				result.Members.Add(memberModel);
			}
		}

		return result;
	}

	private string ParseTypeToBuilder(ItemModel? type, int indent, string typeName, string stringMethod, string valueMethod, out int holeCount, out int literalCount)
	{
		holeCount = 0;
		literalCount = 0;

		var result = new IndentedStringBuilder(String.Empty, "\t");

		result.AppendLine($"{typeName}.{stringMethod}(\"<{type.RootName ?? type.TypeName}\");");

		foreach (var attribute in type.Members.Where(w => w.Attribute?.AttributeType == AttributeType.Attribute))
		{
			result.AppendLine($"{typeName}.{stringMethod}(\" {attribute.Attribute.Name ?? attribute.Name}=\");");
			result.AppendLine($"{typeName}.{valueMethod}(value.{attribute.Name});");
			result.AppendLine($"""{typeName}.{stringMethod}("\"");""");

			literalCount += 4 + (attribute.Attribute.Name ?? attribute.Name).Length;
			holeCount++;
		}

		result.AppendLine($"""{typeName}.{stringMethod}(">");""");
		result.AppendLine($"""{typeName}.{stringMethod}(Environment.NewLine);""");
		literalCount += 3;

		var indentString = new string(' ', indent * 2);

		foreach (var attribute in type.Members.Where(w => w.Attribute?.AttributeType == AttributeType.Element))
		{
			var indentText = attribute.Type.IsClass ? "\t" : String.Empty;

			if (attribute.Type.IsClass)
			{
				result.AppendLine($"if (value.{attribute.Name} != null)");
				result.AppendLine("{");
			}

			result.AppendLine($"""{indentText}{typeName}.{stringMethod}("{indentString}<{attribute.Attribute.Name ?? attribute.Name}>");""");
			literalCount += 2 + indentString.Length + (attribute.Attribute.Name ?? attribute.Name).Length;

			if (attribute.Type.SpecialType == SpecialType.System_String)
			{
				result.AppendLine($"{indentText}{typeName}.{stringMethod}(value.{attribute.Name});");
			}
			else
			{
				result.AppendLine($"{indentText}{typeName}.{valueMethod}(value.{attribute.Name});");
			}

			result.AppendLine($"""{indentText}{typeName}.{stringMethod}("<{attribute.Attribute.Name ?? attribute.Name}>");""");
			result.AppendLine($"""{indentText}{typeName}.{stringMethod}(Environment.NewLine);""");
			literalCount += 3 + indentString.Length + (attribute.Attribute.Name ?? attribute.Name).Length;

			if (attribute.Type.IsClass)
			{
				result.AppendLine("}");
			}

			holeCount++;
		}

		result.Append($"{typeName}.{stringMethod}(\"</{type.RootName ?? type.TypeName}>\");");
		literalCount += 3 + (type.RootName ?? type.TypeName).Length;

		return result.ToString();
	}

	private bool IsValidType(SpecialType type)
	{
		return type is not SpecialType.System_Boolean and not
			SpecialType.System_Byte and not
			SpecialType.System_Char and not
			SpecialType.System_Decimal and not
			SpecialType.System_Double and not
			SpecialType.System_Int16 and not
			SpecialType.System_Int32 and not
			SpecialType.System_Int64 and not
			SpecialType.System_SByte and not
			SpecialType.System_Single and not
			SpecialType.System_String and not
			SpecialType.System_UInt16 and not
			SpecialType.System_UInt32 and not
			SpecialType.System_DateTime and not
			SpecialType.System_Enum and not
			SpecialType.System_UInt64;
	}

	private bool IsCollectionType(SpecialType type)
	{
		return type is SpecialType.System_Array or
			SpecialType.System_Collections_Generic_IEnumerable_T or
			SpecialType.System_Collections_Generic_IList_T or
			SpecialType.System_Collections_Generic_IReadOnlyCollection_T or
			SpecialType.System_Collections_Generic_ICollection_T;
	}
}