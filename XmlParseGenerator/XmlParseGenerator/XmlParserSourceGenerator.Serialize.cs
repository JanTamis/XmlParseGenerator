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
		if (type is null || result.ContainsKey(type.TypeName) || !IsValidType(type.SpecialType) || type.CollectionType != CollectionType.None)
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
	
	private string CreateSerializeForType(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync ? "async Task" : "void";
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var rootName = type.Attributes.TryGetValue(AttributeType.Root, out var rootAttribute) && rootAttribute.ConstructorArguments.Count > 0
			? rootAttribute.ConstructorArguments[0].Value.ToString()
			: type.TypeName;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {async} Serialize{type.TypeName}{asyncSuffix}(XmlWriter writer, {type.TypeName} item)");
		using (_ = builder.IndentBlock())
		{
			builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{rootName}\", null);");
			builder.AppendLine();
			
			// foreach (var attribute in members[AttributeType.Attribute])
			// {
			// 	if (type.IsClass)
			// 	{
			// 		builder.AppendLine($"{asyncKeyword}writer.WriteAttributeString{asyncSuffix}(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}?.ToString());");
			// 	}
			// 	else
			// 	{
			// 		builder.AppendLine($"{asyncKeyword}writer.WriteAttributeString{asyncSuffix}(null, \"{attribute.Attribute.Name ?? attribute.Name}\", null, item.{attribute.Name}.ToString());");
			// 	}
			// }
			//
			// if (members[AttributeType.Attribute].Any())
			// {
			// 	builder.AppendLine();
			// }

			foreach (var element in type.Members)
			{
				var name = element.Type.TypeName;
				
				if (element.Attributes.TryGetValue(AttributeType.Element, out var elementAttribute) && elementAttribute.ConstructorArguments.Count > 0)
				{
					name = elementAttribute.ConstructorArguments[0].Value.ToString();
				}
				
				var elementName = "item";
				var classCheck = $"{elementName}.{element.Name}";

				if (element.Type.CollectionType != CollectionType.None)
				{
					if (element.Type.IsClass)
					{
						builder.AppendLine($"if (item.{element.Name} != null)");
						builder.AppendLine("{");
						builder.Indent();
					}
					builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{name}\", null);");
					builder.AppendLine();

					builder.AppendLine($"foreach (var element in item.{element.Name})");
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
						
						var tempType = element.Type.CollectionItemType ?? element.Type; 
						
						using (_ = builder.IndentBlock())
						{
							builder.AppendLine($"{asyncKeyword}Serialize{tempType.TypeName}{asyncSuffix}(writer, {classCheck});");
						}
					}
					else
					{
						builder.AppendLine($"{asyncKeyword}Serialize{element.Type.TypeName}{asyncSuffix}(writer, {elementName}.{element.Name});");
					}
				}
				else
				{
					builder.AppendLine($"{asyncKeyword}writer.WriteStartElement{asyncSuffix}(null, \"{name}\", null);");

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

		return builder.ToString();
	}
}