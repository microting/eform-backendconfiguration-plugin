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
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

    public static class BackendConfigurationSeedAreas
    {
        public static List<Area> AreasSeed => new()
        {
            new Area
            {
                Id = 1,
                Name = "01. Environmental Management (kun IE-husdyrbrug)",
                Description = @"https://www.microting.dk/eform/landbrug/01-milj%C3%B8ledelse",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 2,
                Name = "02. Contingency",
                Description = @"https://www.microting.dk/eform/landbrug/02-beredskab",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 3,
                Name = "03. Slurry tanks",
                Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                Type = AreaTypesEnum.Type2,
            },
            new Area
            {
                Id = 4,
                Name = "04. Feeding documentation (kun IE-husdyrbrug)",
                Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 5,
                Name = "05. Stable preparations and tail bite documentation",
                Description = @"https://www.microting.dk/eform/landbrug/05-klarg%C3%B8ring-af-stalde-og-dokumentation-af-halebid",
                Type = AreaTypesEnum.Type3,
            },
            new Area
            {
                Id = 6,
                Name = "06. Silos",
                Description = @"https://www.microting.dk/eform/landbrug/06-fodersiloer",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 7,
                Name = "07. Pest control",
                Description = @"https://www.microting.dk/eform/landbrug/07-skadedyr",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 8,
                Name = "08. Aircleaning",
                Description = @"https://www.microting.dk/eform/landbrug/08-luftrensning",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 9,
                Name = "09. Acidification",
                Description = @"https://www.microting.dk/eform/landbrug/09-forsuring",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 10,
                Name = "10. Heat pumps",
                Description = @"https://www.microting.dk/eform/landbrug/10-varmepumper",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 11,
                Name = "11. Pellot stoves",
                Description = @"https://www.microting.dk/eform/landbrug/11-pillefyr",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 12,
                Name = "12. Environmentally hazardous substances",
                Description = @"https://www.microting.dk/eform/landbrug/12-milj%C3%B8farlige-stoffer",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 13,
                Name = "13. Work Place Assesment",
                Description = @"https://www.microting.dk/eform/landbrug/13-apv",
                Type = AreaTypesEnum.Type4,
            },
            new Area
            {
                Id = 14,
                Name = "14. Machines",
                Description = @"https://www.microting.dk/eform/landbrug/14-maskiner",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 15,
                Name = "15. Inspection of power tools",
                Description = @"https://www.microting.dk/eform/landbrug/15-elv%C3%A6rkt%C3%B8j",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 16,
                Name = "16. Inspection of wagons",
                Description = @"https://www.microting.dk/eform/landbrug/16-stiger",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 17,
                Name = "17. Inspection of ladders",
                Description = @"https://www.microting.dk/eform/landbrug/17-brandslukkere",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 18,
                Name = "18. Alarm",
                Description = @"https://www.microting.dk/eform/landbrug/18-alarm",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 19,
                Name = "19. Ventilation",
                Description = @"https://www.microting.dk/eform/landbrug/19-ventilation",
                Type = AreaTypesEnum.Type1,
            },
            new Area
            {
                Id = 20,
                Name = "20. Recurring tasks (mon-sun)",
                Description = @"https://www.microting.dk/eform/landbrug/20-arbejdsopgaver",
                Type = AreaTypesEnum.Type5,
            },
            new Area
            {
                Id = 21,
                Name = "21. DANISH Standard",
                Description = @"https://www.microting.dk/eform/landbrug/21-danish-produktstandard",
                Type = AreaTypesEnum.Type4,
            },
            new Area
            {
                Id = 22,
                Name = "22. Sieve test",
                Description = @"https://www.microting.dk/eform/landbrug/22-sigtetest",
            },
            new Area
            {
                Id = 23,
                Name = "23. Water consumption",
                Description = @"https://www.microting.dk/eform/landbrug/23-vandforbrug",
            },
            new Area
            {
                Id = 24,
                Name = "24. Electricity consumption",
                Description = @"https://www.microting.dk/eform/landbrug/24-elforbrug",
            },
            new Area
            {
                Id = 25,
                Name = "25. Field irrigation consumption",
                Description = @"https://www.microting.dk/eform/landbrug/25-markvandingsforbrug",
            },
            new Area
            {
                Id = 26,
                Name = "100. Miscellaneous",
                Description = @"https://www.microting.dk/eform/landbrug/100-diverse",
            },
        };
    }
}
