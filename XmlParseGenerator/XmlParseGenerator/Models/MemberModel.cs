using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using XmlParseGenerator.Enumerable;

namespace XmlParseGenerator.Models;

public class MemberModel
{
	public string Name { get; set; }
	public string Type { get; set; }
	public bool IsClass { get; set; }
	
	public MemberType MemberType { get; set; }

	public AttributeModel Attribute { get; set; }
	
	public SpecialType SpecialType { get; set; }
}