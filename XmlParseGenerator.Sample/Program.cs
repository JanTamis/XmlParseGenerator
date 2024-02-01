using System;
using System.Threading.Tasks;
using XmlParseGenerator.Sample;

public class Program
{
	private async static Task Main()
	{
		var model = new Test
		{
			FirstName = "Jan Tamis",
			LastName = "Kossen",
		};

		await TestSerializer.SerializeAsync(Console.Out, model);
	}
}