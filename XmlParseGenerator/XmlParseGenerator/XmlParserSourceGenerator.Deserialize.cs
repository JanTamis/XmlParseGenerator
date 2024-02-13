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
			result.Add($"{type.TypeName}{type.CollectionType}", type.CollectionType switch
			{
				CollectionType.List => CreateDeserializeForTypeList(type, false),
				CollectionType.Enumerable => CreateDeserializeForTypeEnumerable(type, false),
				CollectionType.Array => CreateDeserializeForTypeArray(type, false),
			});

			result.Add($"{type.TypeName}{type.CollectionType}Async", type.CollectionType switch
			{
				CollectionType.List => CreateDeserializeForTypeList(type, true),
				CollectionType.Enumerable => CreateDeserializeForTypeEnumerable(type, true),
				CollectionType.Array => CreateDeserializeForTypeArray(type, true),
			});
		}

		foreach (var member in type?.Members ?? System.Linq.Enumerable.Empty<MemberModel>())
		{
			if (member.Type.CollectionType != CollectionType.None)
			{
				if (!result.ContainsKey($"{member.Type.TypeName}{member.Type.CollectionType}"))
				{
					result.Add($"{type.TypeName}{type.CollectionType}", member.Type.CollectionType switch
					{
						CollectionType.List => CreateDeserializeForTypeList(type, false),
						CollectionType.Enumerable => CreateDeserializeForTypeEnumerable(type, false),
						CollectionType.Array => CreateDeserializeForTypeArray(type, false),
					});

					result.Add($"{member.Type.TypeName}{member.Type.CollectionType}Async", member.Type.CollectionType switch
					{
						CollectionType.List => CreateDeserializeForTypeList(type, true),
						CollectionType.Enumerable => CreateDeserializeForTypeEnumerable(type, true),
						CollectionType.Array => CreateDeserializeForTypeArray(type, true),
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
		var members = type.Members.ToLookup(g => g.Attribute?.AttributeType);

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			builder.AppendLine($"var result = new {type.TypeName}();");
			builder.AppendLine();
			using (builder.IndentBlock($"while ({asyncKeyword}reader.Read{asyncSuffix}())"))
			{
				using (builder.IndentScope("if (reader.Depth != depth || !reader.IsStartElement())"))
				{
					builder.AppendLine("break;");
				}

				builder.AppendLine();
				using (builder.IndentBlock("switch (reader.Name)"))
				{
					foreach (var element in members[AttributeType.Element])
					{
						using (builder.IndentScope($"case \"{element.Attribute.Name}\":"))
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
									using (builder.IndentBlock("if (!reader.IsEmptyElement)"))
									{
										builder.AppendLine($"result.{element.Name} = {asyncKeyword}Deserialize{element.Type.TypeName}{element.Type.CollectionType}{asyncSuffix}(reader, depth + 1);");
									}
								}
								else
								{
									builder.AppendLine($"result.{element.Name} = {asyncKeyword}Deserialize{element.Type.TypeName}{asyncSuffix}(reader, depth + 1);");
								}
							}
							
							builder.AppendLine($"{asyncKeyword}reader.Skip{asyncSuffix}();");
							builder.AppendLine("break;");
						}
					}
					
					using (builder.IndentScope("default:"))
					{
						builder.AppendLine($"{asyncKeyword}reader.Skip{asyncSuffix}();");
						builder.AppendLine("break;");
					}
				}
			}
			builder.AppendLine();
			builder.AppendLine("return result;");
		}

		return builder.ToString();
	}
}