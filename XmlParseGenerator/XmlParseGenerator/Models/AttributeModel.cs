using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using XmlParseGenerator.Enumerable;

namespace XmlParseGenerator.Models;

public class AttributeModel
{
	public Dictionary<string, TypedConstant> NamedParameters { get; set; } = new();
	public List<TypedConstant> ConstructorArguments { get; set; } = new();
}