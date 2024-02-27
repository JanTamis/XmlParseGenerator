// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;

namespace XmlParseGenerator.Sample;

[Serializable]
[XmlRoot("person")]
public class Test // : IXmlSerializable
{
	[XmlElement("firstName")]
	[DefaultValue("test")]
	public string FirstName { get; set; }

	[XmlElement("lastName")]
	public string LastName { get; set; }
	
	[XmlElement("age")]
	[DefaultValue(18)]
	public int Age { get; set; }

	[XmlArray("children")]
	[XmlArrayItem(ElementName = "child")]
	public List<Test> Children { get; set; }
}