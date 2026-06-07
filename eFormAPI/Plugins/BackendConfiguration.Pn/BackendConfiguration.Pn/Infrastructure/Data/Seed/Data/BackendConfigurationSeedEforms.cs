/*
The MIT License (MIT)

Copyright (c) 2007 - 2021 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data;

using System.Collections.Generic;

public static class BackendConfigurationSeedEforms
{
	public static List<KeyValuePair<string, List<string>>> GetForms()
	{
		var theList = new List<KeyValuePair<string, List<string>>>();

		var headers = new List<string> {"05.Halebid", ""};
		var item = new KeyValuePair<string, List<string>>("05. Halebid og risikovurdering", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("03. Kontrol flydelag", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("01. Ny opgave", headers);
		theList.Add(item);
		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("02. Igangværende opgave", headers);
		theList.Add(item);

		// Commented out as it is not used in the current version
		// headers = ["", "", ""];
		// item = new KeyValuePair<string, List<string>>("25.01 Registrer produkter", headers);
		// theList.Add(item);
		// headers = ["", "", ""];
		// item = new KeyValuePair<string, List<string>>("25.02 Vis kemisk produkt", headers);
		// theList.Add(item);
		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("00. Info boks", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("Kvittering", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("01. Standard", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("02. Flydelag beholder", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("03. Konstruktion beholder", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("04. Aktivitet beholder", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("05. Kontrol telt", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("06. Numerisk", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("09. Medarbejder (APV-new)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("10. Anhugningsgrej (APV)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("11. Brandslukkere (APV)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("12. Elværktøj (APV)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("14. Hejseredskaber og spil (APV)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("15. Løfteredskaber (APV)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("16. Maskiner (APV)", headers);
		theList.Add(item);

		headers = ["", "", ""];
		item = new KeyValuePair<string, List<string>>("17. Stiger (APV)", headers);
		theList.Add(item);

		return theList;
	}
}