using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XmlParseGenerator.Enumerable;
using XmlParseGenerator.Models;

namespace XmlParseGenerator;

[Generator]
public partial class XmlParserSourceGenerator : IIncrementalGenerator
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
			.Append("System.Collections.Generic")
			.Append("System")
			.Append("System.Buffers")
			.Append("System.IO")
			.Append("System.Linq")
			.Append("System.Text")
			.Append("System.Globalization")
			.Append("System.Threading.Tasks")
			.Append("System.Runtime.CompilerServices")
			.Append("System.Xml")
			.Append("System.Xml.Serialization")
			.Distinct()
			.OrderBy(o => o)
			.Select(s => $"using {s};\n");

		var serializerTypes = new Dictionary<string, string>();
		var deserializerTypes = new Dictionary<string, string>();

		CreateSerializeForType(serializerTypes, type);
		CreateDeserializeForType(deserializerTypes, type);

		var rootName = type.Attributes.TryGetValue(AttributeType.Root, out var rootAttribute) && rootAttribute[0].ConstructorArguments.Count > 0
			? rootAttribute[0].ConstructorArguments[0].Value?.ToString()
			: type.TypeName;

		var defaultWriterSettings = type.HasSerializeContent && type.Members.Any()
			? """
				private static XmlWriterSettings defaultWriterSettings = new XmlWriterSettings()
				{
					Indent = true,
					CheckCharacters = false,
				};
			
				private static XmlWriterSettings defaultWriterSettingsAsync = new XmlWriterSettings()
				{
					Indent = true,
					CheckCharacters = false,
					Async = true,
				};
			"""
			: String.Empty;

		var defaultReaderSettings = type.HasSerializeContent && type.Members.Any()
			? """
				private static XmlReaderSettings defaultReaderSettings = new XmlReaderSettings()
				{
					IgnoreComments = true,
					IgnoreWhitespace = true,
					CheckCharacters = false,
					IgnoreProcessingInstructions = true,
					DtdProcessing = DtdProcessing.Ignore,
				};
				
				private static XmlReaderSettings defaultReaderSettingsAsync = new XmlReaderSettings()
				{
					IgnoreComments = true,
					IgnoreWhitespace = true,
					CheckCharacters = false,
					IgnoreProcessingInstructions = true,
					DtdProcessing = DtdProcessing.Ignore,
					Async = true,
				};
			"""
			: String.Empty;

		var code = $$""""
			{{String.Concat(namespaces)}}
			namespace {{type.RootNamespace}};

			#nullable enable

			public static class {{type.TypeName}}Serializer
			{
			{{String.Join("\n\n", ((IEnumerable<string>) [defaultReaderSettings, defaultWriterSettings, ..GetSerializeMethods(type, rootName), ..GetDeserializeMethods(type, rootName), ..serializerTypes.Values.Select(s => '\t' + s), ..deserializerTypes.Values.Select(s => '\t' + s)]).Where(w => !String.IsNullOrWhiteSpace(w)))}}
			}
			"""";

		context.AddSource($"{type.TypeName}Serializer.g.cs", code);
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
			RootNamespace = namedType.ContainingNamespace?.ToDisplayString() ?? String.Empty,
			IsClass = namedType.IsReferenceType,
			SpecialType = namedType.SpecialType,
			Attributes = GetAtributes(namedType),
			HasSerializableInterface = namedType.AllInterfaces.Any(a => a.ToDisplayString() == "System.Xml.Serialization.IXmlSerializable"),
			HasDeserializeContent = true,
			HasSerializeContent = true,
		};

		if (result.HasSerializableInterface)
		{
			var members = namedType.GetMembers()
				.OfType<IMethodSymbol>()
				.Where(w => w.MethodKind is MethodKind.Ordinary or MethodKind.LambdaMethod);

			foreach (var member in members)
			{
				switch (member.Name)
				{
					case "ReadXml":
						result.HasDeserializeContent = member.DeclaringSyntaxReferences
							.OfType<MethodDeclarationSyntax>()
							.Any(a => a.Body is { Statements.Count: > 0 } || a.ExpressionBody is not null);
						break;
					case "WriteXml":
						result.HasSerializeContent = member.DeclaringSyntaxReferences
							.OfType<MethodDeclarationSyntax>()
							.Any(a => a.Body is { Statements.Count: > 0 } || a.ExpressionBody is not null);
						break;
				}
			}
		}

		if (namedType is IArrayTypeSymbol arrayInfo)
		{
			result.TypeName = arrayInfo.ElementType.Name;
			result.RootNamespace = arrayInfo.ElementType.ContainingNamespace.ToDisplayString();
			result.CollectionType = CollectionType.Array;
			result.CollectionName = nameof(CollectionType.Array);
			result.SpecialType = arrayInfo.ElementType.SpecialType;
			result.IsClass = arrayInfo.ElementType.IsReferenceType;

			types.Add($"{result.TypeName}Array", result);

			result.Namespaces.Add(arrayInfo.ElementType.ContainingNamespace.ToDisplayString());
		}
		else if (namedType.TypeKind == TypeKind.Interface)
		{
		}
		else if (namedType is INamedTypeSymbol { TypeArguments.Length: 1 } listType && namedType.AllInterfaces.Any(a => a.Name == "ICollection" && a.ContainingNamespace.ToDisplayString() == "System.Collections.Generic"))
		{
			var type = listType.TypeArguments[0];
			result.CollectionName = listType.Name;
			result.RootNamespace = type.ContainingNamespace.ToDisplayString();
			result.CollectionType = CollectionType.Collection;
			result.SpecialType = type.SpecialType;
			result.IsClass = type.IsReferenceType;
			result.CollectionItemType = FillItemModel(type, types);

			result.Namespaces.Add(listType.ContainingNamespace.ToDisplayString());
			result.Namespaces.Add(type.ContainingNamespace.ToDisplayString());

			types.Add($"{result.TypeName}{result.CollectionName}", result);
		}
		else
		{
			types.Add($"{result.RootNamespace}.{result.TypeName}", result);
			result.Namespaces.Add(namedType.ContainingNamespace.ToDisplayString());
		}

		if (!result.HasSerializableInterface)
		{
			foreach (var member in namedType.GetMembers())
			{
				if (member.GetAttributes().Any(a => a.AttributeClass.ToString() == "System.Xml.Serialization.XmlIgnoreAttribute"))
				{
					continue;
				}
				
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
					memberModel.Attributes = GetAtributes(member);

					result.Members.Add(memberModel);
				}
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

	private Dictionary<AttributeType, List<AttributeModel>> GetAtributes(ISymbol type)
	{
		var result = new Dictionary<AttributeType, List<AttributeModel>>();

		foreach (var attribute in type.GetAttributes())
		{
			var fullName = attribute.AttributeClass?.ToDisplayString();

			var attributeType = fullName switch
			{
				"System.Xml.Serialization.XmlRootAttribute"      => AttributeType.Root,
				"System.Xml.Serialization.XmlTypeAttribute"      => AttributeType.Type,
				"System.Xml.Serialization.XmlElementAttribute"   => AttributeType.Element,
				"System.Xml.Serialization.XmlAttributeAttribute" => AttributeType.Attribute,
				"System.Xml.Serialization.XmlArrayAttribute"     => AttributeType.Array,
				"System.Xml.Serialization.XmlArrayItemAttribute" => AttributeType.ArrayItem,
				"System.Xml.Serialization.XmlEnumAttribute"      => AttributeType.Enum,
				"System.Xml.Serialization.XmlTextAttribute"      => AttributeType.Text,
				"System.Xml.Serialization.XmlIgnoreAttribute"    => AttributeType.Ignore,
				"System.ComponentModel.DefaultValueAttribute"    => AttributeType.DefaultValue,
				_                                                => AttributeType.None,
			};

			if (attributeType == AttributeType.None)
			{
				continue;
			}

			var tempAttribute = new AttributeModel();

			foreach (var attributeNamedArgument in attribute.NamedArguments)
			{
				tempAttribute.NamedParameters.Add(attributeNamedArgument.Key, attributeNamedArgument.Value);
			}

			foreach (var constructorArgument in attribute.ConstructorArguments)
			{
				tempAttribute.ConstructorArguments.Add(constructorArgument);
			}

			if (result.TryGetValue(attributeType, out var attributes))
			{
				attributes.Add(tempAttribute);
			}
			else
			{
				result.Add(attributeType, [tempAttribute]);
			}
		}

		return result;
	}

	private IEnumerable<string> GetSerializeMethods(ItemModel type, string rootName)
	{
		var serializeTextAsync = type.HasSerializableInterface
			? $"SerializeXmlSerializable<{type.TypeName}>(writer, value);"
			: $"await Serialize{type.TypeName}Async(writer, value, \"{rootName}\");";

		var serializeText = type.HasSerializableInterface
			? $"SerializeXmlSerializable<{type.TypeName}>(writer, value);"
			: $"Serialize{type.TypeName}Async(writer, value, \"{rootName}\");";

		var defaultSerializeName = type.HasSerializeContent && type.Members.Any()
			? "defaultWriterSettings"
			: "null";

		yield return $$"""
				public static string Serialize({{type.TypeName}} value)
				{
					return Serialize(value, {{defaultSerializeName}});
				}
			""";

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static string Serialize({{type.TypeName}} value, XmlWriterSettings? settings)
					{
						using var builder = new StringWriter();
						
						Serialize(builder, value, settings);
					
						return builder.ToString();
					}
				""";
		}
		else
		{
			yield return $$""""
					public static string Serialize({{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is { OmitXmlDeclaration: true })
						{
							return String.Empty;
						}
					
						return """
							<?xml version="1.0" encoding="utf-16"?>
							<{{rootName}} />
							""";
					}
				"""";
		}

		yield return $$"""
				public static void Serialize(TextWriter textWriter, {{type.TypeName}} value)
				{
					Serialize(textWriter, value, {{defaultSerializeName}});
				}
			""";

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static void Serialize(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						using var writer = XmlWriter.Create(textWriter, settings);
					
						writer.WriteStartDocument();
						{{serializeText}}
						writer.WriteEndDocument();
					
						writer.Flush();
					}
				""";
		}
		else
		{
			yield return $$""""
					public static void Serialize(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is null || !settings.OmitXmlDeclaration)
						{
							textWriter.Write($"""
								<?xml version="1.0" encoding="{textWriter.Encoding.WebName}"?>
								<{{rootName}} />
								""");
						}
					}
				"""";
		}

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static void Serialize(Stream stream, {{type.TypeName}} value)
					{
						using var writer = new StreamWriter(stream);
						Serialize(writer, value);
					}
				""";
		}
		else
		{
			yield return $$""""
					public static void Serialize(Stream stream, {{type.TypeName}} value)
					{
						if (stream.CanWrite)
						{
							stream.Write("""
								<?xml version="1.0" encoding="utf-8"?>
								<{{rootName}} />
								"""u8);
						}
					}
				"""";
		}

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static void Serialize(Stream stream, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						using var writer = XmlWriter.Create(stream, settings);
					
						writer.WriteStartDocument();
						{{serializeText}}
						writer.WriteEndDocument();
					}
				""";
		}
		else
		{
			yield return $$""""
					public static void Serialize(Stream stream, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (stream.CanWrite && (settings is null || !settings.OmitXmlDeclaration))
						{
							stream.Write("""
								<?xml version="1.0" encoding="utf-8"?>
								<{{rootName}} />
								"""u8);
						}
					}
				"""";
		}

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static async Task<string> SerializeAsync({{type.TypeName}} value)
					{
						return await SerializeAsync(value, defaultWriterSettings);
					}
				""";
		}
		else
		{
			yield return $$""""
					public static Task<string> SerializeAsync({{type.TypeName}} value)
					{
						return Task.FromResult("""
							<?xml version="1.0" encoding="utf-16"?>
							<{{rootName}} />
							""");
					}
				"""";
		}

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static async Task<string> SerializeAsync({{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is null)
						{
							settings = defaultWriterSettingsAsync;
						}
						
						settings.Async = true;
					
						using var builder = new StringWriter();
						
						await SerializeAsync(builder, value, settings);
					
						return builder.ToString();
					}
				""";
		}
		else
		{
			yield return $$""""
					public static Task<string> SerializeAsync({{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is { OmitXmlDeclaration: true })
						{
							return Task.FromResult(String.Empty);
						}
					
						return Task.FromResult("""
							<?xml version="1.0" encoding="utf-16"?>
							<{{rootName}} />
							""");
					}
				"""";
		}

		yield return $$"""
				public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value)
				{
					await SerializeAsync(textWriter, value, {{defaultSerializeName}});
				}
			""";

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is null)
						{
							settings = defaultWriterSettingsAsync;
						}
							
						settings.Async = true;
						
						await using var writer = XmlWriter.Create(textWriter, settings);
						
						await writer.WriteStartDocumentAsync();
						{{serializeTextAsync}}
						await writer.WriteEndDocumentAsync();
						
						await writer.FlushAsync();
					}
				""";
		}
		else
		{
			yield return $$""""
					public static async Task SerializeAsync(TextWriter textWriter, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is null || !settings.OmitXmlDeclaration)
						{
							await textWriter.WriteAsync($"""
								<?xml version="1.0" encoding="{textWriter.Encoding.WebName}"?>
								<{{rootName}} />
								""");
						}
					}
				"""";
		}


		yield return $$"""
				public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value)
				{
					await SerializeAsync(stream, value, {{defaultSerializeName}});
				}
			""";

		if (type.HasSerializeContent && type.Members.Any())
		{
			yield return $$"""
					public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (settings is null)
						{
							settings = defaultWriterSettingsAsync;
						}
							
						settings.Async = true;
					
						await using var writer = XmlWriter.Create(stream, settings);
					
						await writer.WriteStartDocumentAsync();
						{{serializeTextAsync}}
						await writer.WriteEndDocumentAsync();
					
						await writer.FlushAsync();
					}
				""";
		}
		else
		{
			yield return $$""""
					public static async Task SerializeAsync(Stream stream, {{type.TypeName}} value, XmlWriterSettings? settings)
					{
						if (stream.CanWrite && (settings is null || !settings.OmitXmlDeclaration))
						{
							await stream.WriteAsync("""
								<?xml version="1.0" encoding="utf-8"?>
								<{{rootName}} />
								"""u8.ToArray());
						}
					}
				"""";
		}
	}

	private IEnumerable<string> GetDeserializeMethods(ItemModel type, string rootName)
	{
		var deserializeTextAsync = type.HasSerializableInterface
			? $"DeserializeXmlSerializable<{type.TypeName}>(reader);"
			: $"await Deserialize{type.TypeName}Async(reader, 1);";

		var deserializeText = type.HasSerializableInterface
			? $"DeserializeXmlSerializable<{type.TypeName}>(reader);"
			: $"Deserialize{type.TypeName}(reader, 1);";

		if (type.HasDeserializeContent && type.Members.Any())
		{
			yield return $$"""
					public static {{type.TypeName}} Deserialize(string value)
					{
						var reader = XmlReader.Create(new StringReader(value), defaultReaderSettings);
						                              
						while (reader.Read())
						{
					    if (reader.Depth == 0 && reader.IsStartElement() && reader.Name == "{{rootName}}")
							{
							  return {{deserializeText}}
							}
						}
						
						return new {{type.TypeName}}();
					}
				""";
		}
		else
		{
			yield return $$"""
					public static {{type.TypeName}} Deserialize(string value)
					{
						return new {{type.TypeName}}();
					}
				""";
		}

		if (type.HasDeserializeContent && type.Members.Any())
		{
			yield return $$"""
					public static async Task<{{type.TypeName}}> DeserializeAsync(string value)
					{
						var reader = XmlReader.Create(new StringReader(value), defaultReaderSettingsAsync);
						                              
						while (await reader.ReadAsync())
						{
					    if (reader.Depth == 0 && reader.IsStartElement() && reader.Name == "{{rootName}}")
					    {
					      return {{deserializeTextAsync}}
					    }
						}
						
						return new {{type.TypeName}}();
					}
				""";
		}
		else
		{
			yield return $$"""
					public static Task<{{type.TypeName}}> DeserializeAsync(string value)
					{
						return Task.FromResult(new {{type.TypeName}}());
					}
				""";
		}
	}

	private void AppendSwitchStatement(IndentedStringBuilder builder, string name, List<KeyValuePair<string, string>> statements, string operation = "==", string? defaultStatement = null)
	{
		switch (statements.Count)
		{
			case 0:
				if (defaultStatement != null)
				{
					builder.AppendLine(defaultStatement);
				}
				break;
			case 1:
			{
				using (builder.IndentBlock($"if ({name} {operation} {statements[0].Key})"))
				{
					builder.AppendLine(statements[0].Value);
				}

				if (defaultStatement != null)
				{
					using (builder.IndentBlock("else"))
					{
						builder.AppendLine(defaultStatement);
					}
				}

				break;
			}

			default:
			{
				using (builder.IndentBlock($"switch ({name})"))
				{
					foreach (var statement in statements)
					{
						using (builder.IndentScope($"case {statement.Key}:"))
						{
							builder.AppendLine(statement.Value);
							builder.AppendLine("break;");
						}
					}

					if (defaultStatement != null)
					{
						using (builder.IndentBlock("default:"))
						{
							builder.AppendLine(defaultStatement);
							builder.AppendLine("break;");
						}
					}
				}

				break;
			}
		}
	}

	private string GetFriendlyName(SpecialType type, string fallback)
	{
		return type switch
		{
			SpecialType.System_Boolean => "bool",
			SpecialType.System_Byte => "byte",
			SpecialType.System_Char => "char",
			SpecialType.System_Decimal => "decimal",
			SpecialType.System_Double => "double",
			SpecialType.System_Int16 => "short",
			SpecialType.System_Int32 => "int",
			SpecialType.System_Int64 => "long",
			SpecialType.System_SByte => "sbyte",
			SpecialType.System_Single => "single",
			SpecialType.System_String => "string",
			SpecialType.System_UInt16 => "ushort",
			SpecialType.System_UInt32 => "uint",
			SpecialType.System_UInt64 => "ulong",
			_ => fallback,
		};
	}
}