using System;
using XmlParseGenerator.Enumerable;
using XmlParseGenerator.Models;

namespace XmlParseGenerator;

public partial class XmlParserSourceGenerator
{
	private string CreateDeserializeForTypeArray(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		return CreateCollection(type, isAsync, isAsync
				? $"async Task<{type.TypeName}[]>"
				: $"{type.TypeName}[]",
			initialize: (builder) =>
			{
				builder.AppendLine("var index = 0;");
				builder.AppendLine($"var buffer = ArrayPool<{type.TypeName}>.Shared.Rent(4);");
			},
			body: (builder) =>
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
			},
			returnStatement: (builder) =>
			{
				builder.AppendLine("var result = buffer[..index];");
				builder.AppendLine($"ArrayPool<{type.TypeName}>.Shared.Return(buffer);");
				builder.AppendLine();
				builder.AppendLine("return result;");
			});
	}


	private string CreateDeserializeForTypeList(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		return CreateCollection(type, isAsync, isAsync 
				? $"async Task<{type.CollectionName}<{type.TypeName}>>" 
				: $"{type.CollectionName}<{type.TypeName}>",
			initialize: (builder) => builder.AppendLine($"var result = new {type.CollectionName}<{type.TypeName}>();"),
			body: (builder) => builder.AppendLine($"result.Add({asyncKeyword}Deserialize{type.TypeName}{asyncSuffix}(reader, depth + 1));"),
			returnStatement: (builder) => builder.AppendLine("return result;"));
	}

	private string CreateDeserializeForTypeHashSet(ItemModel? type, bool isAsync)
	{
		if (type is null)
		{
			return String.Empty;
		}

		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		return CreateCollection(type, isAsync, isAsync
				? $"async Task<HashSet<{type.TypeName}>>"
				: $"HashSet<{type.TypeName}>",
			initialize: (builder) => builder.AppendLine($"var result = new List<{type.TypeName}>();"),
			body: (builder) => builder.AppendLine($"result.Add({asyncKeyword}Deserialize{type.TypeName}{asyncSuffix}(reader, depth + 1));"),
			returnStatement: (builder) => builder.AppendLine("return result;"));
	}

	private string CreateCollection(ItemModel? type, bool isAsync, string resultType, Action<IndentedStringBuilder> initialize, Action<IndentedStringBuilder> body, Action<IndentedStringBuilder> returnStatement)
	{
		var asyncKeyword = isAsync ? "await " : String.Empty;
		var asyncSuffix = isAsync ? "Async" : String.Empty;

		var builder = new IndentedStringBuilder("\t", "\t");

		builder.AppendLineWithoutIndent($"private static {resultType} Deserialize{type.TypeName}{type.CollectionName}{asyncSuffix}(XmlReader reader, int depth)");
		using (builder.IndentBlockNoNewline())
		{
			initialize(builder);
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
					body(builder);
				}
			}
			builder.AppendLine();
			returnStatement(builder);
		}

		return builder.ToString();
	}
}