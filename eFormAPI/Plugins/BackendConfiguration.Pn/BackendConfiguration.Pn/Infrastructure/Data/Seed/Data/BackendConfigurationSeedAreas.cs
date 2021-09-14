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
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    public static class BackendConfigurationSeedAreas
    {
        public static IEnumerable<Areas> AreasSeed => new[]
        {
            new Areas
            {
                Id = 1,
                Name = "Environmental Management (kun IE-husdyrbrug)",
                Description = @"https://www.microting.dk/eform/landbrug/01-milj%C3%B8ledelse",
            },
            new Areas
            {
                Id = 2,
                Name = "Contingency",
                Description = @"https://www.microting.dk/eform/landbrug/02-beredskab",
            },
            new Areas
            {
                Id = 3,
                Name = "Slurry tanks",
                Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
            },
            new Areas
            {
                Id = 4,
                Name = "Feeding documentation (kun IE-husdyrbrug)",
                Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
            },
            new Areas
            {
                Id = 5,
                Name = "Stable preparations and tail bite documentation",
                Description = @"https://www.microting.dk/eform/landbrug/05-klarg%C3%B8ring-af-stalde-og-dokumentation-af-halebid",
            },
            new Areas
            {
                Id = 6,
                Name = "Silos",
                Description = @"https://www.microting.dk/eform/landbrug/06-fodersiloer",
            },
            new Areas
            {
                Id = 7,
                Name = "Pest control",
                Description = @"https://www.microting.dk/eform/landbrug/07-skadedyr",
            },
            new Areas
            {
                Id = 8,
                Name = "Aircleaning",
                Description = @"https://www.microting.dk/eform/landbrug/08-luftrensning",
            },
            new Areas
            {
                Id = 9,
                Name = "Acidification",
                Description = @"https://www.microting.dk/eform/landbrug/09-forsuring",
            },
            new Areas
            {
                Id = 10,
                Name = "Heat pumps",
                Description = @"https://www.microting.dk/eform/landbrug/10-varmepumper",
            },
            new Areas
            {
                Id = 11,
                Name = "Pellot stoves",
                Description = @"https://www.microting.dk/eform/landbrug/11-pillefyr",
            },
            new Areas
            {
                Id = 12,
                Name = "Environmentally hazardous substances",
                Description = @"https://www.microting.dk/eform/landbrug/12-milj%C3%B8farlige-stoffer",
            },
            new Areas
            {
                Id = 13,
                Name = "Work Place Assesment",
                Description = @"https://www.microting.dk/eform/landbrug/13-apv",
            },
            new Areas
            {
                Id = 14,
                Name = "Machines",
                Description = @"https://www.microting.dk/eform/landbrug/14-maskiner",
            },
            new Areas
            {
                Id = 15,
                Name = "Inspection of power tools",
                Description = @"https://www.microting.dk/eform/landbrug/15-elv%C3%A6rkt%C3%B8j",
            },
            new Areas
            {
                Id = 16,
                Name = "Inspection of wagons",
                Description = @"https://www.microting.dk/eform/landbrug/16-stiger",
            },
            new Areas
            {
                Id = 17,
                Name = "Inspection of ladders",
                Description = @"https://www.microting.dk/eform/landbrug/17-brandslukkere",
            },
            new Areas
            {
                Id = 18,
                Name = "Alarm",
                Description = @"https://www.microting.dk/eform/landbrug/18-alarm",
            },
            new Areas
            {
                Id = 19,
                Name = "Ventilation",
                Description = @"https://www.microting.dk/eform/landbrug/19-ventilation",
            },
            new Areas
            {
                Id = 20,
                Name = "Recurring tasks (mon-sun)",
                Description = @"https://www.microting.dk/eform/landbrug/20-arbejdsopgaver",
            },
            new Areas
            {
                Id = 21,
                Name = "DANISH Standard",
                Description = @"https://www.microting.dk/eform/landbrug/21-danish-produktstandard",
            },
            new Areas
            {
                Id = 22,
                Name = "Sieve test",
                Description = @"https://www.microting.dk/eform/landbrug/22-sigtetest",
            },
            new Areas
            {
                Id = 23,
                Name = "Water consumption",
                Description = @"https://www.microting.dk/eform/landbrug/23-vandforbrug",
            },
            new Areas
            {
                Id = 24,
                Name = "Electricity consumption",
                Description = @"https://www.microting.dk/eform/landbrug/24-elforbrug",
            },
            new Areas
            {
                Id = 25,
                Name = "Field irrigation consumption",
                Description = @"https://www.microting.dk/eform/landbrug/25-markvandingsforbrug",
            },
            new Areas
            {
                Id = 26,
                Name = "Environmental Management",
                Description = @"",
            },
            new Areas
            {
                Id = 27,
                Name = "Feeding documentation",
                Description = @"",
            },
            new Areas
            {
                Id = 28,
                Name = "Stable preparations and tail bite",
                Description = @"",
            },
            new Areas
            {
                Id = 29,
                Name = "Slurry cooling",
                Description = @"",
            },
            new Areas
            {
                Id = 30,
                Name = "Miscellaneous",
                Description = @"https://www.microting.dk/eform/landbrug/100-diverse",
            },
        };
    }
}
