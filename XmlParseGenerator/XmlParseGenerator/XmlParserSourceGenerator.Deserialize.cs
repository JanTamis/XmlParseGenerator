using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using XmlParseGenerator.Enumerable;
using XmlParseGenerator.Models;

namespace XmlParseGenerator;

public partial class XmlParserSourceGenerator
{
	private void CreateDeserializeForType(Dictionary<string, string> result, ItemModel? type)
	{
		if (type is null || result.ContainsKey(type.TypeName) || type.SpecialType != SpecialType.None)
		{
			return;
		}

		result.Add(type.TypeName, CreateDeserializeForType(type, false));
		result.Add($"{type.TypeName}Async", CreateDeserializeForType(type, true));

		if (type.CollectionType != CollectionType.None)
		{
			result.Add($"{type.TypeName}{type.CollectionName}", type.CollectionType switch
			{
				CollectionType.Collection => CreateDeserializeForTypeCollection(type, false),
				CollectionType.Array      => CreateDeserializeForTypeArray(type, false),
			});

			result.Add($"{type.TypeName}{type.CollectionName}Async", type.CollectionType switch
			{
				CollectionType.Collection => CreateDeserializeForTypeCollection(type, true),
				CollectionType.Array      => CreateDeserializeForTypeArray(type, true),
			});
		}

		foreach (var member in type?.Members ?? System.Linq.Enumerable.Empty<MemberModel>())
		{
			if (member.Type.CollectionType != CollectionType.None)
			{
				if (!result.ContainsKey($"{member.Type.TypeName}{member.Type.CollectionName}"))
				{
					result.Add($"{member.Type.TypeName}{member.Type.CollectionName}", member.Type.CollectionType switch
					{
						CollectionType.Collection => CreateDeserializeForTypeCollection(member.Type, false),
						CollectionType.Array      => CreateDeserializeForTypeArray(member.Type, false),
					});

					result.Add($"{member.Type.TypeName}{member.Type.CollectionName}Async", member.Type.CollectionType switch
					{
						CollectionType.Collection => CreateDeserializeForTypeCollection(member.Type, true),
						CollectionType.Array      => CreateDeserializeForTypeArray(member.Type, true),
					});
				}
			}
			else if (!result.ContainsKey(member.Type.TypeName))
			{
				CreateDeserializeForType(result, member?.Type);
			}
		}
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

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			builder.AppendLine($"var result = new {type.TypeName}();");
			builder.AppendLine();

			if (type.HasSerializableInterface)
			{
				builder.AppendLine("result.ReadXml(reader);");
				builder.AppendLine();
			}
			else
			{
				if (type.Members.Any(a => a.Attributes.ContainsKey(AttributeType.Attribute)))
			{
				using (builder.IndentBlock("if (reader.HasAttributes)"))
				{
					using (builder.IndentBlock("while (reader.MoveToNextAttribute())"))
					{
						using (builder.IndentBlock("switch (reader.Name)"))
						{
							foreach (var member in type.Members)
							{
								if (member.Attributes.TryGetValue(AttributeType.Attribute, out var attributeModel))
								{
									using (builder.IndentScope($"case \"{attributeModel.ConstructorArguments[0].Value}\":"))
									{
										if (member.Type.SpecialType == SpecialType.System_String)
										{
											builder.AppendLine($"result.{member.Name} = reader.Value;");
										}
										else
										{
											builder.AppendLine($"result.{member.Name} = XmlConvert.To{member.Type.TypeName}(reader.Value);");
										}
										builder.AppendLine("break;");
									}
								}
							}
						}
					}
				}

				builder.AppendLine();
			}

			using (builder.IndentBlock($"while ({asyncKeyword}reader.Read{asyncSuffix}())"))
			{
				using (builder.IndentScope("if (reader.Depth != depth || !reader.IsStartElement() || reader.IsEmptyElement)"))
				{
					builder.AppendLine("break;");
				}

				builder.AppendLine();
				using (builder.IndentBlock("switch (reader.Name)"))
				{
					foreach (var element in type.Members.Where(w => !w.Attributes.ContainsKey(AttributeType.Attribute)))
					{
						var name = element.Name;

						if (element.Attributes.TryGetValue(AttributeType.Element, out var attribute))
						{
							name = attribute.ConstructorArguments[0].Value.ToString();
						}

						if (element.Type.CollectionType != CollectionType.None && element.Attributes.TryGetValue(AttributeType.Array, out var arrayAttribute))
						{
							name = arrayAttribute.ConstructorArguments[0].Value.ToString();
						}

						using (builder.IndentScope($"case \"{name}\":"))
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
								if (element.Type.CollectionType != CollectionType.None)
								{
									var elementName = element.Type.TypeName;
									
									if (element.Attributes.TryGetValue(AttributeType.ArrayItem, out var arrayItemAttribute) && arrayItemAttribute.NamedParameters.TryGetValue("ElementName", out var result))
									{
										elementName = result.Value.ToString();
									}
									
									builder.AppendLine($"result.{element.Name} = {asyncKeyword}Deserialize{element.Type.CollectionItemType.TypeName}{element.Type.TypeName}{asyncSuffix}(reader, depth + 1, \"{elementName}\");");
								}
								else
								{
									builder.AppendLine($"result.{element.Name} = {asyncKeyword}Deserialize{element.Type.TypeName}{asyncSuffix}(reader, depth + 1);");
								}
							}

							// builder.AppendLine($"{asyncKeyword}reader.Skip{asyncSuffix}();");
							builder.AppendLine("break;");
						}
					}

					// using (builder.IndentScope("default:"))
					// {
					// 	builder.AppendLine($"{asyncKeyword}reader.Skip{asyncSuffix}();");
					// 	builder.AppendLine("break;");
					// }
				}

				builder.AppendLine();
				builder.AppendLine($"{asyncKeyword}reader.Skip{asyncSuffix}();");
			}
			}

			

			builder.AppendLine();
			builder.AppendLine("return result;");
		}

		return builder.ToString();
	}
}