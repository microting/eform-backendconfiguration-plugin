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

namespace BackendConfiguration.Pn.Infrastructure.Data.Seed.Data
{
	using System.Collections.Generic;

	public static class BackendConfigurationSeedEforms
	{
		public static List<KeyValuePair<string, List<string>>> GetForms()
		{
			var theList = new List<KeyValuePair<string, List<string>>>();
			var headers = new List<string>
			{
				"01. Aflæsning Miljøledelse","01.01 Elforbrug"
			};
			var item = new KeyValuePair<string, List<string>>("01. Elforbrug", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"", ""
			};
			item = new KeyValuePair<string, List<string>>("01. Miljøledelse_skabelon", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"01. Aflæsning Miljøledelse","01.02 Vandforbrug"
			};
			item = new KeyValuePair<string, List<string>>("01. Vandforbrug", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"02. Beredskab","02.01 Brandudstyr"
			};
			item = new KeyValuePair<string, List<string>>("02. Brandudstyr", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"02. Beredskab","02.02 Førstehjælpsudstyr"
			};
			item = new KeyValuePair<string, List<string>>("02. Førstehjælp", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"02. Beredskab","02.03 Sikkerhedsudstyr og værnemidler"
			};
			item = new KeyValuePair<string, List<string>>("02. Sikkerhedsudstyr_værnemidler", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"03. Gyllebeholdere","03.02 Alarm"
			};
			item = new KeyValuePair<string, List<string>>("03. Kontrol alarmanlæg gyllebeholder", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"03. Gyllebeholdere","03.01 Flydelag"
			};
			item = new KeyValuePair<string, List<string>>("03. Kontrol flydelag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"03. Gyllebeholdere","03.03 Konstruktion"
			};
			item = new KeyValuePair<string, List<string>>("03. Kontrol konstruktion", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"04. Foderindlægssedler",""
			};
			item = new KeyValuePair<string, List<string>>("04. Foderindlægssedler", headers);
			theList.Add(item);
			// headers = new List<string>
			// {
			// 	"05. Klargøring af stalde og dokumentation af halebid","05.01 Halebid"
			// };
			// item = new KeyValuePair<string, List<string>>("05. Halebid", headers);
			// theList.Add(item);
			headers = new List<string>
			{
				"05. Klargøring af stalde og dokumentation af halebid","05.02 Klargøring af stalde"
			};
			item = new KeyValuePair<string, List<string>>("05. Stald_klargøring", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"06. Fodersiloer",""
			};
			item = new KeyValuePair<string, List<string>>("06. Siloer", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"07. Dokumentation skadedyrsbekæmpelse","07.01 Fluer"
			};
			item = new KeyValuePair<string, List<string>>("07. Fluer", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"07. Dokumentation skadedyrsbekæmpelse","07.02 Rotter"
			};
			item = new KeyValuePair<string, List<string>>("07. Rotter", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"08. Luftrensning","08.01 Driftsstop"
			};
			item = new KeyValuePair<string, List<string>>("08. Luftrensning driftsstop", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"08. Luftrensning","08.02 Serviceaftale"
			};
			item = new KeyValuePair<string, List<string>>("08. Luftrensning serviceaftale", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"08. Luftrensning","08.03 Timer"
			};
			item = new KeyValuePair<string, List<string>>("08. Luftrensning timer", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"09. Forsuring","09.01 Driftsstop"
			};
			item = new KeyValuePair<string, List<string>>("09. Forsuring driftsstop", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"09. Forsuring","09.03 pH værdi"
			};
			item = new KeyValuePair<string, List<string>>("09. Forsuring pH værdi", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"09. Forsuring","09.02 Serviceaftale"
			};
			item = new KeyValuePair<string, List<string>>("09. Forsuring serviceaftale", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"10. Varmepumpe","10.01 Driftsstop"
			};
			item = new KeyValuePair<string, List<string>>("10. Varmepumpe driftsstop", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"10. Varmepumpe","10.02 Serviceaftale"
			};
			item = new KeyValuePair<string, List<string>>("10. Varmepumpe serviceaftale", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"10. Varmepumpe","10.03 Timer og energi"
			};
			item = new KeyValuePair<string, List<string>>("10. Varmepumpe timer og energi", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"11. Pillefyr",""
			};
			item = new KeyValuePair<string, List<string>>("11. Pillefyr", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"12. Miljøfarlige stoffer","12.01 Affald og farligt affald"
			};
			item = new KeyValuePair<string, List<string>>("12. Affald og farligt affald", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"12. Miljøfarlige stoffer","12.02 Dieseltank"
			};
			item = new KeyValuePair<string, List<string>>("12. Dieseltank", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"12. Miljøfarlige stoffer","12.03 Kemi"
			};
			item = new KeyValuePair<string, List<string>>("12. Kemi", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"12. Miljøfarlige stoffer","12.04 Motor- og spildolie"
			};
			item = new KeyValuePair<string, List<string>>("12. Motor- og spildolie", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"",""
			};
			item = new KeyValuePair<string, List<string>>("13. APV Handlingsplan", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"",""
			};
			item = new KeyValuePair<string, List<string>>("13. APV Medarbejder", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"14. Maskiner",""
			};
			item = new KeyValuePair<string, List<string>>("14. Maskiner", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"15. Elværktøj",""
			};
			item = new KeyValuePair<string, List<string>>("15. Elværktøj", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"16. Stiger",""
			};
			item = new KeyValuePair<string, List<string>>("16. Stiger", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"17. Håndildslukkere",""
			};
			item = new KeyValuePair<string, List<string>>("17. Håndildslukkere", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"18. Alarm",""
			};
			item = new KeyValuePair<string, List<string>>("18. Alarm", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"19. Ventilation",""
			};
			item = new KeyValuePair<string, List<string>>("19. Ventilation", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"20. Arbejdsopgaver",""
			};
			item = new KeyValuePair<string, List<string>>("20. Arbejdsopgave udført", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"21. DANISH Produktstandard v. 1.01",""
			};
			item = new KeyValuePair<string, List<string>>("21. DANISH Produktstandard v_1_01", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"22. Sigtetest",""
			};
			item = new KeyValuePair<string, List<string>>("22. Sigtetest", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.00Aflæsninger",""
			};
			item = new KeyValuePair<string, List<string>>("23.00.01 Aflæsning vand", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.00Aflæsninger",""
			};
			item = new KeyValuePair<string, List<string>>("23.00.02 Aflæsning el", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.02Gyllekøling"
			};
			item = new KeyValuePair<string, List<string>>("23.01.02 Gyllekøling", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.03Forsuring"
			};
			item = new KeyValuePair<string, List<string>>("23.01.03 Forsuring", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.04Ugentlig udslusning af gylle"
			};
			item = new KeyValuePair<string, List<string>>("23.01.04 Ugentlig udslusning af gylle", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.05Punktudsugning i slagtesvinestalde"
			};
			item = new KeyValuePair<string, List<string>>("23.01.05 Punktudsugning i slagtesvinestalde", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.06Varmevekslere til traditionelle slagtekyllingestalde"
			};
			item = new KeyValuePair<string, List<string>>("23.01.06 Varmevekslere til traditionelle slagtekyllingestalde", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.07Gødningsbånd til æglæggende høns"
			};
			item = new KeyValuePair<string, List<string>>("23.01.07 Gødningsbånd til æglæggende høns", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.08Biologisk luftrensning"
			};
			item = new KeyValuePair<string, List<string>>("23.01.08 Biologisk luftrensning", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.09Kemisk luftrensning"
			};
			item = new KeyValuePair<string, List<string>>("23.01.09 Kemisk luftrensning", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.01Årlig visuel kontrol af gyllebeholdere"
			};
			item = new KeyValuePair<string, List<string>>("23.02.01 Årlig visuel kontrol af gyllebeholdere", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.02Gyllepumper mm."
			};
			item = new KeyValuePair<string, List<string>>("23.02.02 Gyllepumper mm", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.03Forsyningssystemer til vand og foder"
			};
			item = new KeyValuePair<string, List<string>>("23.02.03 Forsyningssystemer til vand og foder", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.04Varme-, køle- og ventilationssystemer"
			};
			item = new KeyValuePair<string, List<string>>("23.02.04 Varme-, køle- og ventilationssystemer", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.05Siloer og materiel i transportudstyr i forbindelse med foderanlæg (Rør, snegle mv.)"
			};
			item = new KeyValuePair<string, List<string>>("23.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.06Luftrensningssystemer"
			};
			item = new KeyValuePair<string, List<string>>("23.02.06 Luftrensningssystemer", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.07Udstyr til drikkevand"
			};
			item = new KeyValuePair<string, List<string>>("23.02.07 Udstyr til drikkevand", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.08Maskiner til udbringning af husdyrgødning samt doseringsmekanisme"
			};
			item = new KeyValuePair<string, List<string>>("23.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.03Dokumentation for miljøledelse","23.03.01Miljøledelse"
			};
			item = new KeyValuePair<string, List<string>>("23.03.01 Miljøledelse", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.04Overholdelse af fodringskrav","23.04.01Fasefodring"
			};
			item = new KeyValuePair<string, List<string>>("23.04.01 Fasefodring", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.04Overholdelse af fodringskrav","23.04.02Reduceret indhold af råprotein"
			};
			item = new KeyValuePair<string, List<string>>("23.04.02 Reduceret indhold af råprotein", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.04Overholdelse af fodringskrav","23.04.03Tilsætningsstoffer i foder (Fytase eller andet)"
			};
			item = new KeyValuePair<string, List<string>>("23.04.03 Tilsætningsstoffer i foder - fytase eller andet", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.01Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.01 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.02Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.02 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.03Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.03 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.04Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.04 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.05Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.05 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.06Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.06 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.07Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.07 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.08Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.08 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.09Manglende bilag"
			};
			item = new KeyValuePair<string, List<string>>("23.05.09 Manglende bilag", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.01Fast overdækning gyllebeholder"
			};
			item = new KeyValuePair<string, List<string>>("23.01.01 Fast overdækning gyllebeholder", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("01. Ny opgave", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("02. Igangværende opgave", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("03. Afslutted opgave", headers);
			theList.Add(item);

			return theList;
		}
	}
}