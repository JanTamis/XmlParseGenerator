using System;
using System.Threading.Tasks;
using XmlParseGenerator.Sample;

public partial class Program
{
	private static async Task Main()
	{
		var model = new Test
		{
			//FirstName = "Jan Tamis",
			LastName = "Kossen",
		};

		await TestSerializer.SerializeAsync(Console.Out, model);
	}
}