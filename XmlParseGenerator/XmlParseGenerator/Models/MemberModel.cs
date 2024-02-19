using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using XmlParseGenerator.Enumerable;

namespace XmlParseGenerator.Models;

public class MemberModel
{
	public string Name { get; set; }
	public ItemModel? Type { get; set; }
	
	public MemberType MemberType { get; set; }

	public Dictionary<AttributeType, AttributeModel> Attributes { get; set; }
}