using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using XmlParseGenerator.Enumerable;
using XmlParseGenerator.Models;

namespace XmlParseGenerator;

public partial class XmlParserSourceGenerator
{
	private void CreateSerializeForType(Dictionary<string, string> result, ItemModel? type)
	{
		if (type is null || result.ContainsKey(type.TypeName) || !IsValidType(type.SpecialType) || type.CollectionType != CollectionType.None || !type.Members.Any())
		{
			return;
		}

		if (type.HasSerializableInterface)
		{
			if (type.HasSerializeContent)
			{
				var builder = new IndentedStringBuilder("\t", "\t");

				builder.AppendLineWithoutIndent("private static void SerializeXmlSerializable<T>(XmlWriter writer, T item) where T : IXmlSerializable");
				using (builder.IndentBlockNoNewline())
				{
					builder.AppendLine("item.WriteXml(writer);");
				}

				result.Add("IXmlSerializable", builder.ToString());
			}
		}
		else
		{
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

		if (type.Members.Any() && !result.ContainsKey("#WriteElement"))
		{
			result.Add("#WriteElement", """
				private static void WriteElement<T>(XmlWriter writer, string elementName, T value)
					{
						writer.WriteStartElement(null, elementName, null);
						writer.WriteString(value.ToString());
						writer.WriteEndElement();
					}
				""");
		}
		
		if (type.Members.Any() && !result.ContainsKey("#WriteElementAsync"))
		{
			result.Add("#WriteElementAsync", """
				private static async Task WriteElementAsync<T>(XmlWriter writer, string elementName, T value)
					{
						await writer.WriteStartElementAsync(null, elementName, null);
						await writer.WriteStringAsync(value.ToString());
						await writer.WriteEndElementAsync();
					}
				""");
		}
	}

	private string CreateSerializeForType(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync
			? "async Task"
			: "void";
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {async} Serialize{type.TypeName}{asyncSuffix}(XmlWriter writer, {type.TypeName} item, string rootName)");
		using (_ = builder.IndentBlockNoNewline())
		{
			if (type.HasSerializableInterface)
			{
				builder.AppendLine("item.WriteXml(writer);");
				if (isAsync)
				{
					builder.AppendLine();
					builder.AppendLine("return Task.CompletedTask;");
				}
			}
			else
			{
				builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, rootName, null);");
				builder.AppendLine();

				foreach (var attribute in type.Members.Where(w => w.Attributes.ContainsKey(AttributeType.Attribute)))
				{
					var value = attribute.Attributes.TryGetValue(AttributeType.Attribute, out var attributeModel) && attributeModel.ConstructorArguments.Count > 0
						? attributeModel.ConstructorArguments[0].Value.ToString()
						: attribute.Name;

					if (attribute.Type.IsClass)
					{
						builder.AppendLine($"{asyncKeyword}writer.WriteAttributeString{asyncSuffix}(null, \"{value}\", null, item.{attribute.Name}?.ToString());");
					}
					else
					{
						builder.AppendLine($"{asyncKeyword}writer.WriteAttributeString{asyncSuffix}(null, \"{value}\", null, item.{attribute.Name}.ToString());");
					}
				}

				if (type.Members.Any(a => a.Attributes.ContainsKey(AttributeType.Attribute)))
				{
					builder.AppendLine();
				}

				foreach (var element in type.Members)
				{
					if (element.Attributes.ContainsKey(AttributeType.Attribute))
					{
						continue;
					}

					var name = element.Type.TypeName;

					if (element.Attributes.TryGetValue(AttributeType.Element, out var elementAttribute) && elementAttribute.ConstructorArguments.Count > 0)
					{
						name = elementAttribute.ConstructorArguments[0].Value.ToString();
					}

					var elementName = "item";
					var classCheck = $"{elementName}.{element.Name}";

					if (element.Type.CollectionType != CollectionType.None)
					{
						var collectionName = name;

						if (element.Attributes.TryGetValue(AttributeType.Array, out var arrayAttribute) && arrayAttribute.ConstructorArguments.Count > 0)
						{
							collectionName = arrayAttribute.ConstructorArguments[0].Value.ToString();
						}

						if (element.Type.IsClass)
						{
							builder.AppendLine();
							builder.AppendLine($"if (item.{element.Name} != null)");
							builder.AppendLine("{");
							builder.Indent();
						}

						builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{collectionName}\", null);");
						builder.AppendLine();

						builder.AppendLine($"foreach (var element in item.{element.Name})");
						builder.AppendLine("{");
						builder.Indent();

						elementName = "element";
						classCheck = "element";
					}

					if (IsValidType(element.Type.SpecialType))
					{
						var tempType = element.Type.CollectionItemType ?? element.Type;

						var itemName = element.Attributes.TryGetValue(AttributeType.ArrayItem, out var arrayItemAttribute)
							? arrayItemAttribute.NamedParameters.TryGetValue("ElementName", out var result)
								? result.Value.ToString()
								: tempType.TypeName
							: tempType.TypeName;

						if (element.Type.IsClass)
						{
							using (builder.IndentBlock($"if ({classCheck} != null)"))
							{
								builder.AppendLine($"{asyncKeyword}Serialize{tempType.TypeName}{asyncSuffix}(writer, {classCheck}, \"{itemName}\");");
							}
						}
						else
						{
							builder.AppendLine($"{asyncKeyword}Serialize{element.Type.TypeName}{asyncSuffix}(writer, {elementName}.{element.Name}, \"{itemName}\");");
						}
					}
					else
					{
						if (element.Type.IsClass && element.Attributes.TryGetValue(AttributeType.DefaultValue, out var defaultValueAttribute))
						{
							if (element.Type.SpecialType == SpecialType.System_String)
							{
								builder.AppendLine($"{asyncKeyword}WriteElement{asyncSuffix}(writer, \"{name}\", {elementName}.{element.Name} ?? \"{defaultValueAttribute.ConstructorArguments[0].Value}\");");								
							}
							else
							{
								builder.AppendLine($"{asyncKeyword}WriteElement{asyncSuffix}(writer, \"{name}\", {elementName}.{element.Name} ?? {defaultValueAttribute.ConstructorArguments[0].Value});");
							}
						}
						else
						{
							builder.AppendLine($"{asyncKeyword}WriteElement{asyncSuffix}(writer, \"{name}\", {elementName}.{element.Name});");							
						}
						
						
					}

					if (element.Type.CollectionType != CollectionType.None)
					{
						if (element.Type.IsClass)
						{
							builder.Unindent();
							builder.AppendLine("}");
						}

						builder.AppendLine();
						builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");

						builder.Unindent();
						builder.AppendLine("}");
						builder.AppendLine();
					}
				}

				builder.AppendLine($"{asyncKeyword}writer.WriteEndElement{asyncSuffix}();");
			}
		}

		return builder.ToString();
	}
}