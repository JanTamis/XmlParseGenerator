using System;
using XmlParseGenerator.Models;

namespace XmlParseGenerator;

public partial class XmlParserSourceGenerator
{
	private string CreateDeserializeForTypeEnumerable(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync ? $"async Task<IEnumerable<{type.TypeName}>>" : $"IEnumerable<{type.TypeName}>";
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}Enumerable{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			using (builder.IndentBlock($"while ({asyncKeyword}reader.Read{asyncSuffix}())"))
			{
				using (builder.IndentScope("if (reader.Depth != depth || !reader.IsStartElement())"))
				{
					builder.AppendLine("continue;");
				}
				builder.AppendLine();

				using (builder.IndentBlock($"if (reader.Name == \"{type.RootName}\")"))
				{
					builder.AppendLine($"yield return {asyncKeyword}Deserialize{type.TypeName}{asyncSuffix}(reader, depth + 1);");
				}
			}
		}

		return builder.ToString();
	}

	private string CreateDeserializeForTypeArray(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync ? $"async Task<{type.TypeName}[]>" : $"{type.TypeName}[]";
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}Array{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			builder.AppendLine("var index = 0;");
			builder.AppendLine($"var buffer = ArrayPool<{type.TypeName}>.Shared.Rent(4);");
			builder.AppendLine();
			using (builder.IndentBlock($"while ({asyncKeyword}reader.Read{asyncSuffix}())"))
			{
				using (builder.IndentScope("if (reader.Depth != depth || !reader.IsStartElement())"))
				{
					builder.AppendLine("continue;");
				}

				builder.AppendLine();

				using (builder.IndentBlock($"if (reader.Name == \"{type.RootName}\")"))
				{
					using (builder.IndentBlock("if ((uint)index == (uint)buffer.Length)"))
					{
						builder.AppendLine($"var tempBuffer = ArrayPool<{type.TypeName}>.Shared.Rent(buffer.Length * 2);");
						builder.AppendLine("buffer.CopyTo(tempBuffer, 0);");
						builder.AppendLine($"ArrayPool<{type.TypeName}>.Shared.Return(buffer);");
						builder.AppendLine("buffer = tempBuffer;");
					}
					builder.AppendLine();
					builder.AppendLine($"buffer[index++] = {asyncKeyword}Deserialize{type.TypeName}{asyncSuffix}(reader, depth + 1);");
				}
			}
			builder.AppendLine();
			builder.AppendLine("var result = buffer[..index];");
			builder.AppendLine($"ArrayPool<{type.TypeName}>.Shared.Return(buffer);");
			builder.AppendLine();
			builder.AppendLine("return result;");
		}

		return builder.ToString();
	}


	private string CreateDeserializeForTypeList(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var async = isAsync ? $"async Task<List<{type.TypeName}>>" : $"List<{type.TypeName}>";
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {async} Deserialize{type.TypeName}List{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			builder.AppendLine($"var result = new List<{type.TypeName}>();");
			builder.AppendLine();
			using (builder.IndentBlock($"while ({asyncKeyword}reader.Read{asyncSuffix}())"))
			{
				using (builder.IndentScope("if (reader.Depth != depth || !reader.IsStartElement())"))
				{
					builder.AppendLine("continue;");
				}
				builder.AppendLine();

				using (builder.IndentBlock($"if (reader.Name == \"{type.RootName}\")"))
				{
					builder.AppendLine($"result.Add({asyncKeyword}Deserialize{type.TypeName}{asyncSuffix}(reader, depth + 1));");
				}
			}
			builder.AppendLine();
			builder.AppendLine("return result;");
		}

		return builder.ToString();
	}
}