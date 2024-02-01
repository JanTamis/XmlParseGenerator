using System.Collections.Generic;

namespace XmlParseGenerator.Models;

public record ItemModel
{
	public string TypeName { get; set; }
	public string? RootName { get; set; }
	public string RootNamespace { get; set; }

	public HashSet<string> Namespaces { get; set; } = new();
	
	public List<MemberModel> Members { get; set; } = new();
}