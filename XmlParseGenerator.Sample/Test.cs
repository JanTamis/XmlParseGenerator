// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace XmlParseGenerator.Sample;

[Serializable]
[XmlRoot("person")]
public class Test : IEquatable<Test>
{
	[XmlElement("firstName")]
	public string FirstName { get; set; }

	[XmlElement("lastName")]
	public string LastName { get; set; }
	
	[XmlElement("age")]
	public int Age { get; set; }

	[XmlElement("children")]
	public HashSet<Test> Children { get; set; }

	public bool Equals(Test? other)
	{
		if (other is null)
			return false;

		return this.FirstName == other.FirstName && 
			this.LastName == other.LastName && 
			this.Age == other.Age;
	}
}