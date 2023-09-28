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
			// var headers = new List<string>
			// {
			// 	"01.Aflæsning Miljøledelse","01.01Elforbrug"
			// };
			// var item = new KeyValuePair<string, List<string>>("01. Elforbrug", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"", ""
			// };
			// item = new KeyValuePair<string, List<string>>("01. Miljøledelse_skabelon", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Aflæsning Miljøledelse","01.02Vandforbrug"
			// };
			// item = new KeyValuePair<string, List<string>>("01. Vandforbrug", headers);
			// theList.Add(item);
			// var headers = new List<string>
			// {
			// 	"02.Beredskab","02.01Brandudstyr"
			// };
			// var item = new KeyValuePair<string, List<string>>("02. Brandudstyr", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"02.Beredskab","02.02Førstehjælpsudstyr"
			// };
			// item = new KeyValuePair<string, List<string>>("02. Førstehjælp", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"02.Beredskab","02.03Sikkerhedsudstyr og værnemidler"
			// };
			// item = new KeyValuePair<string, List<string>>("02. Sikkerhedsudstyr_værnemidler", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"03.Gyllebeholdere","03.02Alarm"
			// };
			// item = new KeyValuePair<string, List<string>>("03. Kontrol alarmanlæg gyllebeholder", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"03.Gyllebeholdere","03.01Flydelag"
			// };
			// item = new KeyValuePair<string, List<string>>("03. Kontrol flydelag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"03.Gyllebeholdere","03.03Konstruktion"
			// };
			// item = new KeyValuePair<string, List<string>>("03. Kontrol konstruktion", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"04.Foderindlægssedler",""
			// };
			// item = new KeyValuePair<string, List<string>>("04. Foderindlægssedler", headers);
			// theList.Add(item);
            // headers = new List<string>
            //  {
            //      "05. Klargøring af stalde og dokumentation af halebid","05.01 Halebid"
            //  };
            // item = new KeyValuePair<string, List<string>>("05.01 Halebid", headers);
            // theList.Add(item);
            var headers = new List<string>
             {
                 "05.Halebid",""
             };
            var item = new KeyValuePair<string, List<string>>("05. Halebid og risikovurdering", headers);
            theList.Add(item);
   //          headers = new List<string>
			// {
			// 	"05.Stalde: Halebid og klargøring","05.02Klargøring af stalde"
			// };
			// item = new KeyValuePair<string, List<string>>("05. Stald_klargøring", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"06.Fodersiloer",""
			// };
			// item = new KeyValuePair<string, List<string>>("06. Siloer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"07.Dokumentation skadedyrsbekæmpelse","07.01Fluer"
			// };
			// item = new KeyValuePair<string, List<string>>("07. Fluer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"07.Dokumentation skadedyrsbekæmpelse","07.02Rotter"
			// };
			// item = new KeyValuePair<string, List<string>>("07. Rotter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"08.Luftrensning","08.01Driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("08. Luftrensning driftsstop", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"08.Luftrensning","08.02Serviceaftale"
			// };
			// item = new KeyValuePair<string, List<string>>("08. Luftrensning serviceaftale", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"08.Luftrensning","08.03Timer"
			// };
			// item = new KeyValuePair<string, List<string>>("08. Luftrensning timer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"09.Forsuring","09.01Driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("09. Forsuring driftsstop", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"09.Forsuring","09.03pH værdi"
			// };
			// item = new KeyValuePair<string, List<string>>("09. Forsuring pH værdi", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"09.Forsuring","09.02Serviceaftale"
			// };
			// item = new KeyValuePair<string, List<string>>("09. Forsuring serviceaftale", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"10.Varmepumpe","10.01Driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("10. Varmepumpe driftsstop", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"10.Varmepumpe","10.02Serviceaftale"
			// };
			// item = new KeyValuePair<string, List<string>>("10. Varmepumpe serviceaftale", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"10.Varmepumpe","10.03Timer og energi"
			// };
			// item = new KeyValuePair<string, List<string>>("10. Varmepumpe timer og energi", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"11.Varmekilder",""
			// };
			// item = new KeyValuePair<string, List<string>>("11. Pillefyr", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"12.Miljøfarlige stoffer","12.01Affald og farligt affald"
			// };
			// item = new KeyValuePair<string, List<string>>("12. Affald og farligt affald", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"12.Miljøfarlige stoffer","12.02Dieseltank"
			// };
			// item = new KeyValuePair<string, List<string>>("12. Dieseltank", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"12.Miljøfarlige stoffer","12.03Kemi"
			// };
			// item = new KeyValuePair<string, List<string>>("12. Kemi", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"12.Miljøfarlige stoffer","12.04Motor- og spildolie"
			// };
			// item = new KeyValuePair<string, List<string>>("12. Motor- og spildolie", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"",""
			// };
			// item = new KeyValuePair<string, List<string>>("13. APV Handlingsplan", headers);
			// theList.Add(item);
			headers = new List<string>
			{
				"APV Medarbejder",""
			};
			item = new KeyValuePair<string, List<string>>("13. APV Medarbejder", headers);
			theList.Add(item);
			// headers = new List<string>
			// {
			// 	"14.Maskiner",""
			// };
			// item = new KeyValuePair<string, List<string>>("14. Maskiner", headers);
			// theList.Add(item);
			headers = new List<string>
			{
				"15.Elværktøj",""
			};
			item = new KeyValuePair<string, List<string>>("15. Elværktøj", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"16.Stiger",""
			};
			item = new KeyValuePair<string, List<string>>("16. Stiger", headers);
			theList.Add(item);
			// headers = new List<string>
			// {
			// 	"17.Brandslukkere",""
			// };
			// item = new KeyValuePair<string, List<string>>("17. Brandslukkere", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"18.Alarm",""
			// };
			// item = new KeyValuePair<string, List<string>>("18. Alarm", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"19.Ventilation",""
			// };
			// item = new KeyValuePair<string, List<string>>("19. Ventilation", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"20.Arbejdsopgaver",""
			// };
			// item = new KeyValuePair<string, List<string>>("20. Arbejdsopgave udført", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"21.DANISH Produktstandard v. 1.01",""
			// };
			// item = new KeyValuePair<string, List<string>>("21. DANISH Produktstandard v_1_01", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"22.Sigtetest",""
			// };
			// item = new KeyValuePair<string, List<string>>("22. Sigtetest", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.00Aflæsninger",""
			// };
			// item = new KeyValuePair<string, List<string>>("23.00.01 Aflæsning vand", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.00Aflæsninger",""
			// };
			// item = new KeyValuePair<string, List<string>>("23.00.02 Aflæsning el", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.02Gyllekøling"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.02 Gyllekøling", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.03Forsuring"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.03 Forsuring", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.04Ugentlig udslusning af gylle"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.04 Ugentlig udslusning af gylle", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.05Punktudsugning i slagtesvinestalde"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.05 Punktudsugning i slagtesvinestalde", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.06Varmevekslere til traditionelle slagtekyllingestalde"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.06 Varmevekslere til traditionelle slagtekyllingestalde", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.07Gødningsbånd til æglæggende høns"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.07 Gødningsbånd til æglæggende høns", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.08Biologisk luftrensning"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.08 Biologisk luftrensning", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.09Kemisk luftrensning"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.09 Kemisk luftrensning", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.01Årlig visuel kontrol af gyllebeholdere"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.01 Årlig visuel kontrol af gyllebeholdere", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.02Gyllepumper mm."
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.02 Gyllepumper mm", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.03Forsyningssystemer til vand og foder"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.03 Forsyningssystemer til vand og foder", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.04Varme-, køle- og ventilationssystemer"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.04 Varme-, køle- og ventilationssystemer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.05Siloer og materiel i transportudstyr i forbindelse med foderanlæg (Rør, snegle mv.)"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.06Luftrensningssystemer"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.06 Luftrensningssystemer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.07Udstyr til drikkevand"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.07 Udstyr til drikkevand", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.02Dokumentation for gennemførte kontroller","23.02.08Maskiner til udbringning af husdyrgødning samt doseringsmekanisme"
			// };
			// item = new KeyValuePair<string, List<string>>("23.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.03Dokumentation miljøledelse","23.03.01Miljøledelse"
			// };
			// item = new KeyValuePair<string, List<string>>("23.03.01 Miljøledelse", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.04Overholdelse fodringskrav","23.04.01Fasefodring"
			// };
			// item = new KeyValuePair<string, List<string>>("23.04.01 Fasefodring", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.04Overholdelse fodringskrav","23.04.02Reduceret indhold af råprotein"
			// };
			// item = new KeyValuePair<string, List<string>>("23.04.02 Reduceret indhold af råprotein", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.04Overholdelse fodringskrav","23.04.03Tilsætningsstoffer i foder (Fytase eller andet)"
			// };
			// item = new KeyValuePair<string, List<string>>("23.04.03 Tilsætningsstoffer i foder - fytase eller andet", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.01Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.01 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.02Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.02 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.03Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.03 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.04Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.04 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.05Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.05 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.06Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.06 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.07Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.07 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.08Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.08 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.05Manglende bilag","23.05.09Manglende bilag"
			// };
			// item = new KeyValuePair<string, List<string>>("23.05.09 Manglende bilag", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"23.Indberetning IE Husdyrbrug","23.01Logbøger for eventuel miljøteknologier","23.01.01Fast overdækning gyllebeholder"
			// };
			// item = new KeyValuePair<string, List<string>>("23.01.01 Fast overdækning gyllebeholder", headers);
			// theList.Add(item);
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
			// headers = new List<string>
			// {
			// 	"24.00Aflæsninger","24.00.01Aflæsning vand",""
			// };
			// item = new KeyValuePair<string, List<string>>("24.00.01 Aflæsning vand", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.00Aflæsninger","24.00.02Aflæsning el",""
			// };
			// item = new KeyValuePair<string, List<string>>("24.00.02 Aflæsning el", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.01Gyllebeholdere","24.01.01.01Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.01.01 Gyllebeholdere - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.02Gyllekøling","24.01.02.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.02.01 Gyllekøling - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.02Gyllekøling","24.01.02.02Drift (datalogger, ydelse)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.02.02 Gyllekøling - Drift", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.02Gyllekøling","24.01.02.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.02.03 Gyllekøling - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.03Forsuring","24.01.03.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.03.01 Forsuring - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.03Forsuring","24.01.03.02Drift (datalogger, ydelse)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.03.02 Forsuring - Drift", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.03Forsuring","24.01.03.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.03.03 Forsuring - Dokumenter", headers);
			// theList.Add(item);
			// // Commented pr https://app.userback.io/viewer/33542/62605/2162452EVA7bLB5/
			// // headers = new List<string>
			// // {
			// // 	"24.01Logbøger miljøteknologier","24.01.04Ugentlig udslusning af gylle","24.01.04.01Logbog v. driftstop"
			// // };
			// // item = new KeyValuePair<string, List<string>>("24.01.04.01 Ugentlig udslusning af gylle - Logbog", headers);
			// // theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.04Ugentlig udslusning af gylle","24.01.04.02Udslusning (datalogger, biogas afhentningsrapport)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.04.02 Ugentlig udslusning af gylle - Drift v2", headers);
			// theList.Add(item);
			// // Commented pr https://app.userback.io/viewer/33542/62605/2162452EVA7bLB5/
			// // headers = new List<string>
			// // {
			// // 	"24.01Logbøger miljøteknologier","24.01.04Ugentlig udslusning af gylle","24.01.04.03Dokumenter (reparationer, faktura, service m.m.)"
			// // };
			// // item = new KeyValuePair<string, List<string>>("24.01.04.03 Ugentlig udslusning af gylle - Dokumenter", headers);
			// // theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.05Punktudsugning i slagtesvinestalde","24.01.05.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.05.01 Punktudsugning i slagtesvinestalde - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.05Punktudsugning i slagtesvinestalde","24.01.05.02Drift (datalogger, ydelse)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.05.02 Punktudsugning i slagtesvinestalde - Drift", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.05Punktudsugning i slagtesvinestalde","24.01.05.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.05.03 Punktudsugning i slagtesvinestalde - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.06Varmevekslere til traditionelle slagtekyllingestalde","24.01.06.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.06.01 Varmevekslere til traditionelle slagtekyllingestalde - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.06Varmevekslere til traditionelle slagtekyllingestalde","24.01.06.02Drift og rengøring (datalogger, holdskiftplan)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.06.02 Varmevekslere til traditionelle slagtekyllingestalde - Drift og rengøring", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.06Varmevekslere til traditionelle slagtekyllingestalde","24.01.06.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.06.03 Varmevekslere til traditionelle slagtekyllingestalde - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.07Gødningsbånd til æglæggende høns","24.01.07.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.07.01 Gødningsbånd til æglæggende høns - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.07Gødningsbånd til æglæggende høns","24.01.07.02Drift (datalogger, udmugningskalender)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.07.02 Gødningsbånd til æglæggende høns - Drift", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.07Gødningsbånd til æglæggende høns","24.01.07.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.07.03 Gødningsbånd til æglæggende høns - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.08Biologisk luftrensning","24.01.08.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.08.01 Biologisk luftrensning - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.08Biologisk luftrensning","24.01.08.02Drift (datalogger, ydelse, ledetal)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.08.02 Biologisk luftrensning - Drift", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.08Biologisk luftrensning","24.01.08.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.08.03 Biologisk luftrensning - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.09Kemisk luftrensning","24.01.09.01Logbog v. driftsstop"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.09.01 Kemisk luftrensning - Logbog", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.09Kemisk luftrensning","24.01.09.02Drift (datalogger, ydelse, rensningskalender)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.09.02 Kemisk luftrensning - Drift", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.01Logbøger miljøteknologier","24.01.09Kemisk luftrensning","24.01.09.03Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.01.09.03 Kemisk luftrensning - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.01Visuel kontrol af tom gyllebeholdere","24.02.01.01Tom gyllebeholder gennemgået (spændebånd, kabler m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.01.01 Visuel kontrol af tom gyllebeholdere", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.01Visuel kontrol af tom gyllebeholdere","24.02.01.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.01.02 Visuel kontrol af tom gyllebeholdere - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.02Gyllepumper","24.02.02.01Gyllepumper gennemgået (pumpe, omrører m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.02.01 Gyllepumper", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.02Gyllepumper","24.02.02.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.02.02 Gyllepumper - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.03Forsyningssystemer til vand og foder","24.02.03.01Forsyningssystemer til vand og foder gennemgået (vandforsyning, foderanlæg)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.03.01 Forsyningssystemer til vand og foder", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.03Forsyningssystemer til vand og foder","24.02.03.01Forsyningssystemer til vand og foder gennemgået (vandforsyning, foderanlæg)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.03.02 Forsyningssystemer til vand og foder - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.04Varme-, køle- og ventilationssystemer","24.02.04.01Varme-, køle- og ventilationssystemer gennemgået"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.04.01 Varme-, køle- og ventilationssystemer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.04Varme-, køle- og ventilationssystemer","24.02.04.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.04.02 Varme-, køle- og ventilationssystemer - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.05Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.","24.02.05.01Siloer, rør, snegle mv. gennemgået"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.05.01 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.05Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.","24.02.05.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.05.02 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv. - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.06Luftrensningssystemer","24.02.06.01Luftrensningssystemer gennemgået"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.06.01 Luftrensningssystemer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.06Luftrensningssystemer","24.02.06.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.06.02 Luftrensningssystemer - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.07Udstyr til drikkevand","24.02.07.01Udstyr til drikkevand gennemgået (indstillinger kontrolleres)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.07.01 Udstyr til drikkevand", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.07Udstyr til drikkevand","24.02.07.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.07.02 Udstyr til drikkevand - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.08Maskiner til udbringning af husdyrgødning samt doseringsmekanisme","24.02.08.01Maskiner til udbringning af husdyrgødning samt doseringsmekanisme gennemgået"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.08.01 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.02Dokumentation afsluttede inspektioner","24.02.08Maskiner til udbringning af husdyrgødning samt doseringsmekanisme","24.02.08.02Dokumenter (reparationer, faktura, service m.m.)"
			// };
			// item = new KeyValuePair<string, List<string>>("24.02.08.02 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme - Dokumenter", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.03Dokumentation miljøledelse","24.03.01Evaluering Miljøledelse",""
			// };
			// item = new KeyValuePair<string, List<string>>("24.03.01 Evaluering Miljøledelse", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.03Dokumentation miljøledelse","24.03.01Miljøledelses-dokument",""
			// };
			// item = new KeyValuePair<string, List<string>>("24.03.02 Dokumenter til Miljøledelse", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.04Overholdelse fodringskrav","24.04.01Fasefodring","24.04.01.01Indlægssedler og blanderecepter"
			// };
			// item = new KeyValuePair<string, List<string>>("24.04.01 Fasefodring", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.04Overholdelse fodringskrav","24.04.02Reduceret indhold af råprotein","24.04.02.01Indlægssedler og blanderecepter"
			// };
			// item = new KeyValuePair<string, List<string>>("24.04.02 Reduceret indhold af råprotein", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"24.04Overholdelse fodringskrav","24.04.03Tilsætningsstoffer i foder - fytase eller andet","24.04.03.01Indlægssedler og blanderecepter"
			// };
			// item = new KeyValuePair<string, List<string>>("24.04.03 Tilsætningsstoffer i foder - fytase eller andet", headers);
			// theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("25.01 Registrer produkter", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("25.02 Vis kemisk produkt", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			// item = new KeyValuePair<string, List<string>>("01. Aflæsninger", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"","",""
			// };
			// item = new KeyValuePair<string, List<string>>("02. Fækale uheld", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"","",""
			// };
			// item = new KeyValuePair<string, List<string>>("00. Info boks", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"26.Kornlager","",""
			// };
			// item = new KeyValuePair<string, List<string>>("26. Kornlager", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Morgenrundtur","",""
			// };
			// item = new KeyValuePair<string, List<string>>("00. Morgenrundtur", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","1.1Aflæsning vand",""
			// };
			// item = new KeyValuePair<string, List<string>>("1.1 Aflæsning vand", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","1.2Aflæsning el",""
			// };
			// item = new KeyValuePair<string, List<string>>("1.2 Aflæsning el", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","2.1Udslusning af gylle",""
			// };
			// item = new KeyValuePair<string, List<string>>("2.1 Udslusning af gylle", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","2.2Gyllekøling: Timer og MWh",""
			// };
			// item = new KeyValuePair<string, List<string>>("2.2 Gyllekøling Timer og MWh", headers);
			// theList.Add(item);
			headers = new List<string>
			{
				"01.Logbøger Miljøledelse","2.3Gyllekøling: Driftsstop",""
			};
			item = new KeyValuePair<string, List<string>>("2.3 Gyllekøling Driftsstop", headers);
			theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","2.4Forsuring: pH-værdi",""
			// };
			// item = new KeyValuePair<string, List<string>>("2.4 Forsuring pH-værdi", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","2.5Forsuring: Driftsstop",""
			// };
			// item = new KeyValuePair<string, List<string>>("2.5 Forsuring Driftsstop", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"01.Logbøger Miljøledelse","2.6Foderindlægssedler",""
			// };
			// item = new KeyValuePair<string, List<string>>("2.6 Foderindlægssedler", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","01.Gyllekøling",""
			// };
			// item = new KeyValuePair<string, List<string>>("01. Gyllekøling", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","02.Forsuring",""
			// };
			// item = new KeyValuePair<string, List<string>>("02. Forsuring", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","03.Luftrensning",""
			// };
			// item = new KeyValuePair<string, List<string>>("03. Luftrensning", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","04.Beholderkontrol gennemført",""
			// };
			// item = new KeyValuePair<string, List<string>>("04. Beholderkontrol gennemført", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","05.Gyllebeholdere",""
			// };
			// item = new KeyValuePair<string, List<string>>("05. Gyllebeholdere", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","06.Gyllepumper, - miksere, - seperatorer og spredere",""
			// };
			// item = new KeyValuePair<string, List<string>>("06. Gyllepumper, - miksere, - seperatorer og spredere", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","07.Forsyningssystemer til vand og foder",""
			// };
			// item = new KeyValuePair<string, List<string>>("07. Forsyningssystemer til vand og foder", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","08.Varme-, køle- og ventilationssystemer samt temperaturfølere",""
			// };
			// item = new KeyValuePair<string, List<string>>("08. Varme-, køle- og ventilationssystemer samt temperaturfølere", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","09.Siloer og transportudstyr",""
			// };
			// item = new KeyValuePair<string, List<string>>("09. Siloer og transportudstyr", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","10.Luftrensningssystemer",""
			// };
			// item = new KeyValuePair<string, List<string>>("10. Luftrensningssystemer", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","11.Udstyr til drikkevand",""
			// };
			// item = new KeyValuePair<string, List<string>>("11. Udstyr til drikkevand", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","12.Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse",""
			// };
			// item = new KeyValuePair<string, List<string>>("12. Maskiner til udbringning af husdyrgødning samt doseringsmekanisme- eller dyse", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","13.Miljøledelse gennemgået og revideret",""
			// };
			// item = new KeyValuePair<string, List<string>>("13. Miljøledelse", headers);
			// theList.Add(item);
			// headers = new List<string>
			// {
			// 	"00.Logbøger","14.Beredskabsplan gennemgået og revideret",""
			// };
			// item = new KeyValuePair<string, List<string>>("14. Beredskabsplan", headers);
			// theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("Kvittering", headers);
			theList.Add(item);
			headers = new List<string>
			{
				"","",""
			};
			item = new KeyValuePair<string, List<string>>("Gyllebeholder: Aktivitet i beholder", headers);
			theList.Add(item);

			return theList;
		}
	}
}