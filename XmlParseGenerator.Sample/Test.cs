﻿// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace XmlParseGenerator.Sample;

[Serializable]
[XmlRoot("person")]
public class Test
{
	[XmlElement("firstName")]
	public string FirstName { get; set; }

	[XmlElement("lastName")]
	public string LastName { get; set; }
	
	[XmlAttribute("age")]
	public int Age { get; set; }

	[XmlArray("children")]
	[XmlArrayItem(ElementName = "child")]
	public List<Test> Children { get; set; }
}