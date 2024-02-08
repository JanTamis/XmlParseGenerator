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
			using System.Collections.Generic;
			using System.IO;
			using System.Linq;
			using System.Text;
			using System.Globalization;
			using System.Threading.Tasks;
			using System.Runtime.CompilerServices;
			using System.Xml;
			{{String.Concat(namespaces)}}
			namespace {{type.RootNamespace}};

			#nullable enable

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
					return await SerializeAsync(value, GetDefaultSerializeSettings());
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
					await SerializeAsync(textWriter, value, GetDefaultSerializeSettings());
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
					await SerializeAsync(stream, value, GetDefaultSerializeSettings());
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
					using var xmlReader = XmlReader.Create(new StringReader(value), GetDefaultDeserializeSettings());
					                              
					while (xmlReader.Read())
					{
				    if (xmlReader.Depth != 0 || !xmlReader.IsStartElement())
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
			
				private static XmlWriterSettings GetDefaultSerializeSettings()
				{
					return new XmlWriterSettings
					{
						Indent = true,
					};
				}

				private static XmlReaderSettings GetDefaultDeserializeSettings()
				{
					return new XmlReaderSettings()
					{
						IgnoreComments = true,
						IgnoreWhitespace = true,
					};
				}
			
				{{String.Join("\n\t", serializerTypes.Values)}}
				
				{{String.Join("\n\t", deserializerTypes.Values)}}
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

		if (type.IsCollection)
		{

		}
		else
		{
			result.Add(type.TypeName, CreateSerializeForType(type, false));
			result.Add($"{type.TypeName}Async", CreateSerializeForType(type, true));
		}


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
		if (type is null || result.ContainsKey(type.TypeName) || type.SpecialType != SpecialType.None)
		{
			return;
		}

		result.Add(type.TypeName, CreateDeserializeForType(type, false));
		result.Add($"{type.TypeName}Async", CreateDeserializeForType(type, true));

		if (type.IsCollection)
		{
			result.Add($"{type.TypeName}Collection", CreateDeserializeForTypeCollection(type));
		}

		foreach (var member in type?.Members ?? System.Linq.Enumerable.Empty<MemberModel>())
		{
			if (member.Type.IsCollection && !result.ContainsKey($"{member.Type.TypeName}Collection"))
			{
				result.Add($"{member.Type.TypeName}Collection", CreateDeserializeForTypeCollection(type));
			}
			else if (!result.ContainsKey(member.Type.TypeName))
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
		using (_ = builder.IndentBlock())
		{
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
				var elementName = "item";
				var classCheck = $"{elementName}.{element.Name}";

				if (element.Type.IsCollection)
				{
					var suffix = String.Empty;

					if (element.Type.IsClass)
					{
						suffix = $" ?? Enumerable.Empty<{element.Type.TypeName}>()";
					}

					builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{element.Attribute.Name ?? element.Type.TypeName}\", null);");
					builder.AppendLine();

					builder.AppendLine($"foreach (var element in item.{element.Name}{suffix})");
					builder.AppendLine("{");
					builder.Indent();

					elementName = "element";
					classCheck = "element";
				}

				if (IsValidType(element.Type.SpecialType))
				{
					if (element.Type.IsClass)
					{
						builder.AppendLine($"if ({classCheck} != null)");
						using (_ = builder.IndentBlock())
						{
							builder.AppendLine($"{asyncKeyword}Serialize{element.Type.TypeName}{asyncSuffix}(writer, {classCheck});");
						}

						builder.AppendLine();
					}
					else
					{
						builder.AppendLine($"{asyncKeyword}Serialize{element.Type.TypeName}{asyncSuffix}(writer, {elementName}.{element.Name});");
					}
				}
				else
				{
					builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{element.Attribute.Name ?? element.Name}\", null);");

					if (element.Type.SpecialType == SpecialType.System_String)
					{
						builder.AppendLine($"{asyncKeyword}writer.WriteString{asyncSuffix}({elementName}.{element.Name});");
					}
					else
					{
						builder.AppendLine($"{asyncKeyword}writer.WriteString{asyncSuffix}(XmlConvert.ToString({elementName}.{element.Name}));");
					}

					builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");
					builder.AppendLine();
				}

				if (element.Type.IsCollection)
				{
					builder.AppendLine();
					builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");
					builder.Unindent();
					builder.AppendLine("}");
				}
			}

			builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");
		}

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
		using (_ = builder.IndentBlock())
		{
			builder.AppendLine($"var result = new {type.TypeName}();");
			builder.AppendLine();
			builder.AppendLine($"while ({asyncKeyword}reader.Read{asyncSuffix}())");
			using (_ = builder.IndentBlock())
			{
				builder.AppendLine("if (reader.Depth != depth || !reader.IsStartElement())");
				using (_ = builder.IndentScope())
				{
					builder.AppendLine("continue;");
				}

				builder.AppendLine();

				builder.AppendLine("switch (reader.Name)");
				using (_ = builder.IndentBlock())
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
									builder.AppendLine($"result.{element.Name} = XmlConvert.To{element.Type.TypeName}(reader.Value);");
								}
							}
							else
							{
								if (element.Type.IsCollection)
								{
									builder.AppendLine($"result.{element.Name} = Deserialize{element.Type.TypeName}Collection(reader.ReadSubtree(), depth + 1);");
								}
								else
								{
									builder.AppendLine($"result.{element.Name} = {asyncKeyword}Deserialize{element.Type.TypeName}{asyncSuffix}(reader.ReadSubtree(), depth + 1);");
								}
							}


							builder.AppendLine("break;");
						}
					}
				}
			}
			builder.AppendLine();
			builder.AppendLine("return result;");
		}

		return builder.ToString();
	}

	private string CreateDeserializeForTypeCollection(ItemModel? type)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var builder = new IndentedStringBuilder("\t", "\t");
		var members = type.Members.ToLookup(g => g.Attribute?.AttributeType);

		builder.AppendLineWithoutIndent($"private static IEnumerable<{type.TypeName}> Deserialize{type.TypeName}Collection(XmlReader reader, int depth)");
		using (_ = builder.IndentBlock())
		{
			builder.AppendLine($"while (reader.Read())");
			using (_ = builder.IndentBlock())
			{
				builder.AppendLine("if (reader.Depth != depth || !reader.IsStartElement())");
				using (_ = builder.IndentScope())
				{
					builder.AppendLine("continue;");
				}
				builder.AppendLine();

				builder.AppendLine($"if (reader.Name == \"{type.RootName}\")");
				using (_ = builder.IndentBlock())
				{
					builder.AppendLine($"yield return Deserialize{type.TypeName}(reader.ReadSubtree(), depth + 1);");
				}
			}
		}

		return builder.ToString();
	}

	private ItemModel? FillItemModel(ITypeSymbol? namedType, Dictionary<string, ItemModel> types)
	{
		if (namedType is null)
		{
			return null;
		}

		ItemModel? result;

		if (namedType.ContainingNamespace is null)
		{
			if (types.TryGetValue(namedType.Name, out result))
			{
				return result;
			}
		}
		else if (types.TryGetValue($"{namedType.ContainingNamespace.ToDisplayString()}.{namedType.Name}", out result))
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
			RootNamespace = namedType.ContainingNamespace?.ToDisplayString() ?? String.Empty,
			IsClass = namedType.IsReferenceType,
			SpecialType = namedType.SpecialType,
		};

		if (namedType is IArrayTypeSymbol arrayInfo)
		{
			result.TypeName = arrayInfo.ElementType.Name;
			result.RootNamespace = arrayInfo.ElementType.ContainingNamespace.ToDisplayString();
			result.IsCollection = true;
			result.SpecialType = arrayInfo.ElementType.SpecialType;
			result.IsClass = arrayInfo.ElementType.IsReferenceType;

			types.Add($"{result.TypeName}[]", result);

			result.Namespaces.Add(arrayInfo.ElementType.ContainingNamespace.ToDisplayString());
		}
		else if (namedType.Name == "IEnumerable" && namedType is INamedTypeSymbol ienumerableType)
		{
			var type = ienumerableType.TypeArguments[0];
			result.TypeName = type.Name;
			result.RootNamespace = type.ContainingNamespace.ToDisplayString();
			result.IsCollection = true;
			result.SpecialType = type.SpecialType;
			result.IsClass = type.IsReferenceType;

			types.Add($"{result.TypeName}[]", result);

			result.Namespaces.Add(type.ContainingNamespace.ToDisplayString());
		}
		else
		{
			types.Add($"{result.RootNamespace}.{result.TypeName}", result);
			result.Namespaces.Add(namedType.ContainingNamespace.ToDisplayString());
		}

		foreach (var member in namedType.GetMembers())
		{
			MemberModel? memberModel = null;

			switch (member)
			{
				case IPropertySymbol { CanBeReferencedByName: true, IsStatic: false, IsIndexer: false } property:
					if (property.Type.ContainingNamespace is not null)
					{
						result.Namespaces.Add(property.Type.ContainingNamespace.ToDisplayString());
					}

					memberModel = new MemberModel
					{
						Name = property.Name,
						Type = FillItemModel(property.Type, types),
						MemberType = MemberType.Property,
					};
					break;
				case IFieldSymbol { CanBeReferencedByName: true, IsStatic: false, } field:
					if (field.Type.ContainingNamespace is not null)
					{
						result.Namespaces.Add(field.Type.ContainingNamespace.ToDisplayString());
					}

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

	private bool IsCollectionType(ITypeSymbol type)
	{
		return type.SpecialType is SpecialType.System_Array or
			SpecialType.System_Collections_Generic_IEnumerable_T or
			SpecialType.System_Collections_Generic_IList_T or
			SpecialType.System_Collections_Generic_IReadOnlyCollection_T or
			SpecialType.System_Collections_Generic_ICollection_T || type is IArrayTypeSymbol;
	}
}