using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using XmlParseGenerator.Enumerable;

namespace XmlParseGenerator.Models;

public record ItemModel
{
	public string TypeName { get; set; }
	public string? RootName { get; set; }
	public string RootNamespace { get; set; }
	public bool IsClass { get; set; }

	public SpecialType SpecialType { get; set; }

	public CollectionType CollectionType { get; set; }

	public HashSet<string> Namespaces { get; set; } = new();
	
	public List<MemberModel> Members { get; set; } = new();
}