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
    using System.Linq;
    using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
    using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

    public static class BackendConfigurationSeedAreas
    {
        public static int LastIndexAreaRules => AreasSeed.SelectMany(x => x.AreaRules).Select(x => x.Id).Max();

        public static List<Area> AreasSeed => new()
        {
            new Area
            {
                Id = 1,
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 1,
                        EformName = "01. Vandforbrug",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "01. Vandforbrug" }, // da
                            new() { LanguageId = 2, Name = "01. Water consumption" }, // en
                            new() { LanguageId = 3, Name = "01. Wasserverbrauch" } // ge
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // one
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 2,
                        EformName = "01. Elforbrug",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "01. Elforbrug" },
                            new() { LanguageId = 2, Name = "01. Electricity consumption" },
                            new() { LanguageId = 3, Name = "01. Stromverbrauch" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // one
                            RepeatType = 3, // month
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new() {
                        Name = "01. Miljøledelse (kun IE-husdyrbrug)",
                        Description = @"https://www.microting.dk/eform/landbrug/01-milj%C3%B8ledelse",
                        LanguageId = 1
                    },
                    new() {
                        Name = "01. Environmental Management (kun IE-husdyrbrug)",
                        Description = @"https://www.microting.dk/eform/landbrug/01-milj%C3%B8ledelse",
                        LanguageId = 2
                    },
                    new() {
                        Name = "01. Umweltmanagement (nur IE Tierhaltung)",
                        Description = @"https://www.microting.dk/eform/landbrug/01-milj%C3%B8ledelse",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "01. Vandforbrug",
                    RepeatEvery = 1, // one
                    RepeatType = 3, // month
                }
            },
            new Area
            {
                Id = 2,
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 3,
                        EformName = "02. Brandudstyr",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "02. Brandudstyr" },
                            new() { LanguageId = 2, Name = "02. Fire equipment" },
                            new() { LanguageId = 3, Name = "02. Feuer-Ausrüstung" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 4,
                        EformName = "02. Sikkerhedsudstyr/værnemidler",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "02. Sikkerhedsudstyr/værnemidler" },
                            new() { LanguageId = 2, Name = "02. Safety equipment / protective equipment" },
                            new() { LanguageId = 3, Name = "02. Sicherheitsausrüstung / Schutzausrüstung" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 5,
                        EformName = "02. Førstehjælp",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "02. Førstehjælp" },
                            new() { LanguageId = 2, Name = "02. First aid" },
                            new() { LanguageId = 3, Name = "02. Erste Hilfe" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "02. Beredskab",
                        Description = @"https://www.microting.dk/eform/landbrug/02-beredskab",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "02. Contingency",
                        Description = @"https://www.microting.dk/eform/landbrug/02-beredskab",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "02. Kontingenz",
                        Description = @"https://www.microting.dk/eform/landbrug/02-beredskab",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "02. Brandudstyr",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                }
            },
            new Area
            {
                Id = 3,
                Type = AreaTypesEnum.Type2,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "03. Gylletanke",
                        Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "03. Slurry tanks",
                        Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "03. Gülletanks",
                        Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "03. Kontrol konstruktion",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Alarm = AreaRuleT2AlarmsEnum.No,
                    Type = AreaRuleT2TypesEnum.Closed,
                }
            },
            new Area
            {
                Id = 4,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "04. Feeding documentation (kun IE-husdyrbrug)",
                        Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "04. Feeding documentation (kun IE-livestock only)",
                        Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "04. Fütterungsdokumentation (nur IE Vieh)",
                        Description = @"https://www.microting.dk/eform/landbrug/03-gyllebeholdere",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "04. Foderindlægssedler",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 1, // day
                },
            },
            new Area
            {
                Id = 5,
                Type = AreaTypesEnum.Type3,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "05. Staldforberedelser og halebid dokumentation",
                        Description = @"https://www.microting.dk/eform/landbrug/05-klarg%C3%B8ring-af-stalde-og-dokumentation-af-halebid",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "05. Barn preparations and tail bite documentation",
                        Description = @"https://www.microting.dk/eform/landbrug/05-klarg%C3%B8ring-af-stalde-og-dokumentation-af-halebid",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "05. Stallvorbereitungen und Schwanzbissdokumentation",
                        Description = @"https://www.microting.dk/eform/landbrug/05-klarg%C3%B8ring-af-stalde-og-dokumentation-af-halebid",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "05. Stald_klargøring",
                },
            },
            new Area
            {
                Id = 6,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "06. Siloer",
                        Description = @"https://www.microting.dk/eform/landbrug/06-fodersiloer",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "06. Silos",
                        Description = @"https://www.microting.dk/eform/landbrug/06-fodersiloer",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "06. Silos",
                        Description = @"https://www.microting.dk/eform/landbrug/06-fodersiloer",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "06. Siloer",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3 // month
                }
            },
            new Area
            {
                Id = 7,
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 6,
                        EformName = "07. Rotter",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "07. Rotter" },
                            new() { LanguageId = 2, Name = "07. Rats" },
                            new() { LanguageId = 3, Name = "07. Ratten" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                    new()
                    {
                        Id = 7,
                        EformName = "07. Fluer",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "07. Fluer" },
                            new() { LanguageId = 2, Name = "07. Flies" },
                            new() { LanguageId = 3, Name = "07. Fliegen" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "07. Skadedyrsbekæmpelse",
                        Description = @"https://www.microting.dk/eform/landbrug/07-skadedyr",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "07. Pest control",
                        Description = @"https://www.microting.dk/eform/landbrug/07-skadedyr",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "07. Schädlingsbekämpfung",
                        Description = @"https://www.microting.dk/eform/landbrug/07-skadedyr",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "07. Rotter",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 1 // day
                }
            },
            new Area
            {
                Id = 8,
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 8,
                        EformName = "08. Luftrensning timer",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "08. Luftrensning timer" },
                            new() { LanguageId = 2, Name = "08. Air cleaning timer" },
                            new() { LanguageId = 3, Name = "08. Luftreinigungstimer" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 9,
                        EformName = "08. Luftrensning serviceaftale",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "08. Luftrensning serviceaftale" },
                            new() { LanguageId = 2, Name = "08. Air cleaning service agreement" },
                            new() { LanguageId = 3, Name = "08. Luftreinigungsservicevertrag" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 10,
                        EformName = "08. Luftrensning driftsstop",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "08. Luftrensning driftsstop" },
                            new() { LanguageId = 2, Name = "08. Air cleaning downtime" },
                            new() { LanguageId = 3, Name = "08. Ausfallzeit der Luftreinigung" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "08. Luftrensning",
                        Description = @"https://www.microting.dk/eform/landbrug/08-luftrensning",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "08. Aircleaning",
                        Description = @"https://www.microting.dk/eform/landbrug/08-luftrensning",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "08. Luftreinigung",
                        Description = @"https://www.microting.dk/eform/landbrug/08-luftrensning",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "08. Luftrensning timer",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                }
            },
            new Area
            {
                Id = 9,
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 11,
                        EformName = "09. Forsuring pH værdi",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "09. Forsuring pH værdi" },
                            new() { LanguageId = 2, Name = "09. Acidification pH value" },
                            new() { LanguageId = 3, Name = "09. Ansäuerung pH-Wert" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 12,
                        EformName = "09. Forsuring serviceaftale",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "09. Forsuring serviceaftale" },
                            new() { LanguageId = 2, Name = "09. Acidification service agreement" },
                            new() { LanguageId = 3, Name = "09. Säuerungsservicevertrag" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 13,
                        EformName = "09. Forsuring driftsstop",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "09. Forsuring driftsstop" },
                            new() { LanguageId = 2, Name = "09. Acidification downtime" },
                            new() { LanguageId = 3, Name = "09. Ausfallzeit der Ansäuerung" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "09. Forsuring",
                        Description = @"https://www.microting.dk/eform/landbrug/09-forsuring",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "09. Acidification",
                        Description = @"https://www.microting.dk/eform/landbrug/09-forsuring",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "09. Ansäuerung",
                        Description = @"https://www.microting.dk/eform/landbrug/09-forsuring",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "09. Forsuring pH værdi",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                },
            },
            new Area
            {
                Id = 10,
                Type = AreaTypesEnum.Type6,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 14,
                        EformName = "10. Varmepumpe timer og energi",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "10. Varmepumpe timer og energi" },
                            new() { LanguageId = 2, Name = "10. Heat pumps hours and energy" },
                            new() { LanguageId = 3, Name = "10. Wärmepumpenstunden und Energie" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 15,
                        EformName = "10. Varmepumpe serviceaftale",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "10. Varmepumpe serviceaftale" },
                            new() { LanguageId = 2, Name = "10. Heat pump service agreement" },
                            new() { LanguageId = 3, Name = "10. Servicevertrag für Wärmepumpen" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true,
                        },
                    },
                    new()
                    {
                        Id = 16,
                        EformName = "10. Varmepumpe driftsstop",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "10. Varmepumpe driftsstop" },
                            new() { LanguageId = 2, Name = "10. Heat pump downtime" },
                            new() { LanguageId = 3, Name = "10. Ausfallzeit der Wärmepumpe" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false,
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "10. Varmepumper",
                        Description = @"https://www.microting.dk/eform/landbrug/10-varmepumper",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "10. Heat pumps",
                        Description = @"https://www.microting.dk/eform/landbrug/10-varmepumper",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "10. Wärmepumpen",
                        Description = @"https://www.microting.dk/eform/landbrug/10-varmepumper",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "10. Varmepumpe timer og energi",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                },
            },
            new Area
            {
                Id = 11,
                Type = AreaTypesEnum.Type1,
                AreaInitialField = new AreaInitialField
                {
                    EformName = "11. Pillefyr",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                },
                /*AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 17,
                        EformName = "11. Pillefyr",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "11. Pillefyr" },
                            new() { LanguageId = 2, Name = "11. Pellet stove" },
                            new() { LanguageId = 3, Name = "11. Pelletofen" }
                        },
                    },
                },*/
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "11. Pillefyr",
                        Description = @"https://www.microting.dk/eform/landbrug/11-pillefyr",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "11. Pellet burners",
                        Description = @"https://www.microting.dk/eform/landbrug/11-pillefyr",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "11. Pelletbrenner",
                        Description = @"https://www.microting.dk/eform/landbrug/11-pillefyr",
                        LanguageId = 3
                    }
                }
            },
            new Area
            {
                Id = 12,
                Type = AreaTypesEnum.Type1,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 18,
                        EformName = "12. Dieseltank",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "12. Dieseltank" },
                            new() { LanguageId = 2, Name = "12. Diesel tank" },
                            new() { LanguageId = 3, Name = "12. Dieseltank" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 19,
                        EformName = "12. Motor- og spildolie",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "12. Motor- og spildolie" },
                            new() { LanguageId = 2, Name = "12. Motor oil and waste oil" },
                            new() { LanguageId = 3, Name = "12. Motoröl und Altöl" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 20,
                        EformName = "12. Kemi",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "12. Kemi" },
                            new() { LanguageId = 2, Name = "12. Chemistry" },
                            new() { LanguageId = 3, Name = "12. Chemie" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                        },
                    },
                    new()
                    {
                        Id = 21,
                        EformName = "12. Affald og farligt affald",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "12. Affald og farligt affald" },
                            new() { LanguageId = 2, Name = "12. Trash" },
                            new() { LanguageId = 3, Name = "12. Müll" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 1, // 1
                            RepeatType = 1, // day
                            Notifications = false
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "12. Miljøfarlige stoffer",
                        Description = @"https://www.microting.dk/eform/landbrug/12-milj%C3%B8farlige-stoffer",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "12. Environmentally hazardous substances",
                        Description = @"https://www.microting.dk/eform/landbrug/12-milj%C3%B8farlige-stoffer",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "12. Umweltgefährdende Stoffe",
                        Description = @"https://www.microting.dk/eform/landbrug/12-milj%C3%B8farlige-stoffer",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "12. Dieseltank",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 13,
                Type = AreaTypesEnum.Type4,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 22,
                        EformName = "13. APV Medarbejer",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "13. APV Medarbejer" },
                            new() { LanguageId = 2, Name = "13. WPA Agriculture" },
                            new() { LanguageId = 3, Name = "13. Arbeitsplatz Landwirtschaft" }
                        }
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "13. Arbejdspladsvurdering",
                        Description = @"https://www.microting.dk/eform/landbrug/13-apv",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "13. Work Place Assesment",
                        Description = @"https://www.microting.dk/eform/landbrug/13-apv",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "13. Arbeitsplatzbewertung",
                        Description = @"https://www.microting.dk/eform/landbrug/13-apv",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "13. APV Medarbejer",
                    Notifications = false,
                },
            },
            new Area
            {
                Id = 14,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "14. Maskiner",
                        Description = @"https://www.microting.dk/eform/landbrug/14-maskiner",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "14. Machines",
                        Description = @"https://www.microting.dk/eform/landbrug/14-maskiner",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "14. Machinen",
                        Description = @"https://www.microting.dk/eform/landbrug/14-maskiner",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "14. Maskiner",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 15,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "15. Eftersyn af elværktøj",
                        Description = @"https://www.microting.dk/eform/landbrug/15-elv%C3%A6rkt%C3%B8j",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "15. Inspection of power tools",
                        Description = @"https://www.microting.dk/eform/landbrug/15-elv%C3%A6rkt%C3%B8j",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "15. Inspektion von Elektrowerkzeugen",
                        Description = @"https://www.microting.dk/eform/landbrug/15-elv%C3%A6rkt%C3%B8j",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "15. Elværktøj",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 16,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "16. Stiger",
                        Description = @"https://www.microting.dk/eform/landbrug/16-stiger",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "16. Ladders",
                        Description = @"https://www.microting.dk/eform/landbrug/16-stiger",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "16. Leitern",
                        Description = @"https://www.microting.dk/eform/landbrug/16-stiger",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "16. Stiger",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 17,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "17. Brandslukkere",
                        Description = @"https://www.microting.dk/eform/landbrug/17-brandslukkere",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "17. Fire extinguishers",
                        Description = @"https://www.microting.dk/eform/landbrug/17-brandslukkere",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "17. Feuerlöscher",
                        Description = @"https://www.microting.dk/eform/landbrug/17-brandslukkere",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "17. Håndildslukkere",
                    Notifications = true,
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 18,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "18. Alarm",
                        Description = @"https://www.microting.dk/eform/landbrug/18-alarm",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "18. Alarm",
                        Description = @"https://www.microting.dk/eform/landbrug/18-alarm",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "18. Alarm",
                        Description = @"https://www.microting.dk/eform/landbrug/18-alarm",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "18. Alarm",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 19,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "19. Ventilation",
                        Description = @"https://www.microting.dk/eform/landbrug/19-ventilation",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "19. Ventilation",
                        Description = @"https://www.microting.dk/eform/landbrug/19-ventilation",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "19. Belüftung",
                        Description = @"https://www.microting.dk/eform/landbrug/19-ventilation",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "19. Ventilation",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                },
            },
            new Area
            {
                Id = 20,
                Type = AreaTypesEnum.Type5,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "20. Tilbagevendende opgaver (man-søn)",
                        Description = @"https://www.microting.dk/eform/landbrug/20-arbejdsopgaver",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "20. Recurring tasks (mon-sun)",
                        Description = @"https://www.microting.dk/eform/landbrug/20-arbejdsopgaver",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "20. Wiederkehrende Aufgaben (Mo-So)",
                        Description = @"https://www.microting.dk/eform/landbrug/20-arbejdsopgaver",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "20. Arbejdsopgave udført",
                    RepeatEvery = 7, // 7
                    RepeatType = 1, // days
                }
            },
            new Area
            {
                Id = 21,
                Type = AreaTypesEnum.Type4,
                AreaRules = new List<AreaRule>
                {
                    new()
                    {
                        Id = 23,
                        EformName = "21. DANISH Produktstandard v_1_01",
                        AreaRuleTranslations = new List<AreaRuleTranslation>
                        {
                            new() { LanguageId = 1, Name = "21. DANISH Standard v. 1.01" },
                            new() { LanguageId = 2, Name = "21. DANISH Standard v. 1.01" },
                            new() { LanguageId = 3, Name = "21. DÄNISCHER Standard v. 1.01" }
                        },
                        AreaRuleInitialField = new AreaRuleInitialField
                        {
                            RepeatEvery = 12, // 12
                            RepeatType = 3, // month
                            Notifications = true
                        },
                    },
                },
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "21. DANISH Standard",
                        Description = @"https://www.microting.dk/eform/landbrug/21-danish-produktstandard",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "21. DANISH Standard",
                        Description = @"https://www.microting.dk/eform/landbrug/21-danish-produktstandard",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "21. DANISH Standard",
                        Description = @"https://www.microting.dk/eform/landbrug/21-danish-produktstandard",
                        LanguageId = 3
                    }
                }
            },
            new Area
            {
                Id = 22,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "22. Sigttest",
                        Description = @"https://www.microting.dk/eform/landbrug/22-sigtetest",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "22. Sieve test",
                        Description = @"https://www.microting.dk/eform/landbrug/22-sigtetest",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "22. Testen mit Sieb", // TODO better german translation
                        Description = @"https://www.microting.dk/eform/landbrug/22-sigtetest",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "22. Sigtetest",
                    Notifications = true,
                    RepeatEvery = 14, // 14
                    RepeatType = 1, // days
                },
            },
            new Area
            {
                Id = 23,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "23. Vandforbrug",
                        Description = @"https://www.microting.dk/eform/landbrug/23-vandforbrug",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "23. Water consumption",
                        Description = @"https://www.microting.dk/eform/landbrug/23-vandforbrug",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "23. Wasserverbrauch",
                        Description = @"https://www.microting.dk/eform/landbrug/23-vandforbrug",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "01. Vandforbrug"
                },
            },
            new Area
            {
                Id = 24,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "24. Elforbrug",
                        Description = @"https://www.microting.dk/eform/landbrug/24-elforbrug",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "24. Electricity consumption",
                        Description = @"https://www.microting.dk/eform/landbrug/24-elforbrug",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "24. Stromverbrauch",
                        Description = @"https://www.microting.dk/eform/landbrug/24-elforbrug",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "01. Elforbrug"
                },
            },
            new Area
            {
                Id = 25,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "25. Forbrug af markvanding",
                        Description = @"https://www.microting.dk/eform/landbrug/25-markvandingsforbrug",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "25. Field irrigation consumption",
                        Description = @"https://www.microting.dk/eform/landbrug/25-markvandingsforbrug",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "25. Verbrauch der Feldbewässerung",
                        Description = @"https://www.microting.dk/eform/landbrug/25-markvandingsforbrug",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "25. Markvandingsforbrug"
                },
            },
            new Area
            {
                Id = 26,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new() {
                        Name = "100. Diverse",
                        Description = @"https://www.microting.dk/eform/landbrug/100-diverse",
                        LanguageId = 1
                    },
                    new() {
                        Name = "100. Miscellaneous",
                        Description = @"https://www.microting.dk/eform/landbrug/100-diverse",
                        LanguageId = 2
                    },
                    new() {
                        Name = "100. Sonstig",
                        Description = @"https://www.microting.dk/eform/landbrug/100-diverse",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = ""
                },
            },
        };
    }
}
