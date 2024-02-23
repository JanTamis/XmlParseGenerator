// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace XmlParseGenerator.Sample;

[Serializable]
[XmlRoot("person")]
public class Test : IXmlSerializable
{
	//[XmlElement("firstName")]
	public string FirstName { get; set; }

	//[XmlElement("lastName")]
	public string LastName { get; set; }
	
	//[XmlAttribute("age")]
	public int Age { get; set; }

	//[XmlArray("children")]
	//[XmlArrayItem(ElementName = "child")]
	public List<Test> Children { get; set; }

	public XmlSchema? GetSchema()
	{
		return null;
	}

	public void ReadXml(XmlReader reader)
	{
		
	}

	public void WriteXml(XmlWriter writer)
	{

	}
}