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
		if (type is null || result.ContainsKey(type.TypeName) || type.SpecialType != SpecialType.None || !type.Members.Any())
		{
			return;
		}

		if (type.HasSerializableInterface)
		{
			if (type.HasDeserializeContent)
			{
				var builder = new IndentedStringBuilder("\t", "\t");

				builder.AppendLineWithoutIndent("private static T DeserializeXmlSerializable<T>(XmlReader reader) where T : IXmlSerializable, new()");
				using (builder.IndentBlockNoNewline())
				{
					builder.AppendLine("var result = new T();");
					builder.AppendLine();
					builder.AppendLine("result.ReadXml(reader);");
					builder.AppendLine();
					builder.AppendLine("return result;");
				}

				result.Add("IXmlSerializable", builder.ToString());
			}
		}
		else
		{
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
	}

	private string CreateDeserializeForType(ItemModel? type, bool isAsync)
	{
		if (type is null || (type.HasSerializableInterface && isAsync))
		{
			return String.Empty;
		}

		var async = isAsync
			? $"async Task<{type.TypeName}>"
			: type.TypeName;
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			if (type.Members.Count == 0)
			{
				builder.AppendLine(type.HasSerializableInterface && isAsync
					? $"return Task.FromResult(new {type.TypeName}());"
					: $"return new {type.TypeName}();");
			}
			else
			{
				var defaultValues = type.Members
					.Where(w => w.Attributes.ContainsKey(AttributeType.DefaultValue))
					.Select(s =>
					{
						if (s.Type.SpecialType == SpecialType.System_String)
						{
							return $"{s.Name} = \"{s.Attributes[AttributeType.DefaultValue][0].ConstructorArguments[0].Value}\",";
						}

						return $"{s.Name} = {s.Attributes[AttributeType.DefaultValue][0].ConstructorArguments[0].Value},";
					})
					.ToList();

				if (defaultValues.Any())
				{
					using (builder.IndentBlockNoNewline($"var result = new {type.TypeName}"))
					{
						foreach (var defaultValue in defaultValues)
						{
							builder.AppendLine(defaultValue);
						}
					}

					builder.AppendLineWithoutIndent(";");
				}
				else
				{
					builder.AppendLine($"var result = new {type.TypeName}();");
				}

				builder.AppendLine();

				if (type.HasSerializableInterface)
				{
					builder.AppendLine("result.ReadXml(reader);");
				}
				else
				{
					if (type.Members.Any(a => a.Attributes.ContainsKey(AttributeType.Attribute)))
					{
						using (builder.IndentBlock("if (reader.HasAttributes)"))
						{
							using (builder.IndentBlock("while (reader.MoveToNextAttribute())"))
							{
								AppendSwitchStatement(builder, "reader.Name", type.Members
									.Where(w => w.Attributes.ContainsKey(AttributeType.Attribute))
									.Select(s =>
									{
										if (s.Attributes.TryGetValue(AttributeType.Attribute, out var attributeModel))
										{
											if (s.Type.SpecialType == SpecialType.System_String)
											{
												return new KeyValuePair<string, string>($"\"{attributeModel[0].ConstructorArguments[0].Value}\"", $"result.{s.Name} = reader.Value;");
											}

											return new KeyValuePair<string, string>($"\"{attributeModel[0].ConstructorArguments[0].Value}\"", $"result.{s.Name} = XmlConvert.To{s.Type.TypeName}(reader.Value);");
										}

										return default;
									}).ToList());
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

						builder.AppendLine("var name = reader.Name;");
						builder.AppendLine($"{asyncKeyword}reader.Read{asyncSuffix}();");
						builder.AppendLine();

						AppendSwitchStatement(builder, "name", type.Members
							.Where(w => !w.Attributes.ContainsKey(AttributeType.Attribute) && w.Type.SpecialType != SpecialType.System_Object)
							.Select(s =>
							{
								var name = s.Name;

								if (s.Attributes.TryGetValue(AttributeType.Element, out var attribute))
								{
									name = attribute[0].ConstructorArguments[0].Value.ToString();
								}

								if (s.Type.CollectionType != CollectionType.None && s.Attributes.TryGetValue(AttributeType.Array, out var arrayAttribute))
								{
									name = arrayAttribute[0].ConstructorArguments[0].Value.ToString();
								}

								name = $"\"{name}\"";

								if (!IsValidType(s.Type.SpecialType))
								{
									if (s.Type.SpecialType == SpecialType.System_String)
									{
										return new KeyValuePair<string, string>(name, $"""
											result.{s.Name} = reader.Value;
											""");
									}

									return new KeyValuePair<string, string>(name, $"""
										result.{s.Name} = XmlConvert.To{s.Type.TypeName}(reader.Value);
										""");
								}

								if (s.Type.CollectionType != CollectionType.None)
								{
									var elementName = s.Type.TypeName;

									if (s.Attributes.TryGetValue(AttributeType.ArrayItem, out var arrayItemAttribute) && arrayItemAttribute[0].NamedParameters.TryGetValue("ElementName", out var result))
									{
										elementName = result.Value.ToString();
									}

									return new KeyValuePair<string, string>(name, $"""
										result.{s.Name} = {asyncKeyword}Deserialize{s.Type.CollectionItemType.TypeName}{s.Type.TypeName}{asyncSuffix}(reader, depth + 1, "{elementName}");
										""");
								}

								return new KeyValuePair<string, string>(name, $"""
									result.{s.Name} = {asyncKeyword}Deserialize{s.Type.TypeName}{asyncSuffix}(reader, depth + 1);
									""");
							})
							.ToList());

						builder.AppendLine();
						builder.AppendLine($"{asyncKeyword}reader.Skip{asyncSuffix}();");
					}
				}

				builder.AppendLine();

				builder.AppendLine(type.HasSerializableInterface && isAsync
					? "return Task.FromResult(result);"
					: "return result;");
			}
		}

		return builder.ToString();
	}
}