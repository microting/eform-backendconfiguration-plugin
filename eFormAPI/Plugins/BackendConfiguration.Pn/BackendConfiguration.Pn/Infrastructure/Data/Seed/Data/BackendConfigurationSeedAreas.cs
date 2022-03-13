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
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "01. Registreringer til Miljøledelse",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3xleju932igg",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "01. Registrations to Environmental Management",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3xleju932igg",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "01. Registrierungen zum Umweltmanagement",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3xleju932igg",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "01. Vandforbrug",
                    RepeatEvery = 1, // one
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                }
            },
            new Area
            {
                Id = 2,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "02. Beredskab",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.lxbt89gfjmsr",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "02. Contingency",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.lxbt89gfjmsr",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "02. Kontingenz",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.lxbt89gfjmsr",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "02. Brandudstyr",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 3,
                Type = AreaTypesEnum.Type2,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "03. Gyllebeholdere",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "03. Slurry tanks",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "03. Gülletanks",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589",
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 4,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "04. Fodringskrav (kun IE-husdyrbrug)",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.4a0a8zqjbwmq",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "04. Feeding documentation (kun IE-livestock only)",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.4a0a8zqjbwmq",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "04. Fütterungsdokumentation (nur IE Vieh)",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.4a0a8zqjbwmq",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "04. Foderindlægssedler",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 1, // day
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 5,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "05. Klargøring af stalde og dokumentation af halebid",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "05. Barn preparations and tail bite documentation",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "05. Stallvorbereitungen und Schwanzbissdokumentation",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "05. Stald_klargøring",
                    Notifications = true,
                    ComplianceEnabled = true,
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
                        Name = "06. Fodersiloer",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.9rbo49l8hwn1",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "06. Silos",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.9rbo49l8hwn1",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "06. Silos",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.9rbo49l8hwn1",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "06. Siloer",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 7,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "07. Skadedyrsbekæmpelse",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iljrzeutkuw4",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "07. Pest control",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iljrzeutkuw4",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "07. Schädlingsbekämpfung",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iljrzeutkuw4",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "07. Rotter",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 1, // day
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 8,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "08. Luftrensning",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.q3puu5rb21t5",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "08. Aircleaning",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.q3puu5rb21t5",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "08. Luftreinigung",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.q3puu5rb21t5",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "08. Luftrensning timer",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 9,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "09. Forsuring",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdavny7gjz0n",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "09. Acidification",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdavny7gjz0n",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "09. Ansäuerung",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdavny7gjz0n",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "09. Forsuring pH værdi",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 10,
                Type = AreaTypesEnum.Type6,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "10. Varmepumper",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.dxezyj6gry62",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "10. Heat pumps",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.dxezyj6gry62",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "10. Wärmepumpen",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.dxezyj6gry62",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "10. Varmepumpe timer og energi",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 11,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "11. Varmekilder",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.er2v0a3yxqzu",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "11. Heat sources",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.er2v0a3yxqzu",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "11. Wärmequellen",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.er2v0a3yxqzu",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "11. Varmkilder",
                    Notifications = false,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 12,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "12. Miljøfarlige stoffer",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.ocy0eycm3hu7",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "12. Environmentally hazardous substances",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.ocy0eycm3hu7",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "12. Umweltgefährdende Stoffe",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.ocy0eycm3hu7",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "12. Dieseltank",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 13,
                Type = AreaTypesEnum.Type4,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "13. Arbejdstilsynets Landbrugs APV",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.o3ig9krjpdcb",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "13. The Danish Working Environment Authority's Agricultural APV",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.o3ig9krjpdcb",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "13. Landwirtschaftliche APV . der dänischen Arbeitsumweltbehörde",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.o3ig9krjpdcb",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "13. APV Medarbejder",
                    Notifications = false,
                    ComplianceEnabled = true,
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
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5t8ueh77brvx",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "14. Machines",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5t8ueh77brvx",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "14. Machinen",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5t8ueh77brvx",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "14. Maskiner",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
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
                        Name = "15. Elværktøj",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3aqslige0sx8",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "15. Inspection of power tools",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3aqslige0sx8",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "15. Inspektion von Elektrowerkzeugen",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3aqslige0sx8",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "15. Elværktøj",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
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
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.201m31f6b76t",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "16. Ladders",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.201m31f6b76t",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "16. Leitern",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.201m31f6b76t",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "16. Stiger",
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
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
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5f3qhcjuqopu",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "17. Fire extinguishers",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5f3qhcjuqopu",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "17. Feuerlöscher",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5f3qhcjuqopu",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "17. Brandslukkere",
                    Notifications = true,
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
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
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdl5bg82luo9",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "18. Alarm",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdl5bg82luo9",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "18. Alarm",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdl5bg82luo9",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "18. Alarm",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
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
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.122yhikalodh",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "19. Ventilation",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.122yhikalodh",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "19. Belüftung",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.122yhikalodh",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "19. Ventilation",
                    Notifications = true,
                    RepeatEvery = 1, // 1
                    RepeatType = 3, // month
                    ComplianceEnabled = true,
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
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.7sb10z3swexo",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "20. Recurring tasks (mon-sun)",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.7sb10z3swexo",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "20. Wiederkehrende Aufgaben (Mo-So)",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.7sb10z3swexo",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    EformName = "20. Arbejdsopgave udført",
                    RepeatEvery = 7, // 7
                    RepeatType = 1, // days
                    ComplianceEnabled = false,
                }
            },
            new Area
            {
                Id = 21,
                Type = AreaTypesEnum.Type4,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "21. DANISH Standard",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iw36kvdmxgi8",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "21. DANISH Standard",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iw36kvdmxgi8",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "21. DANISH Standard",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iw36kvdmxgi8",
                        LanguageId = 3
                    }
                },
            },
            new Area
            {
                Id = 22,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "22. Sigtetest",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xawyj9b4y3rq",
                        LanguageId = 1
                    },
                    new()
                    {
                        Name = "22. Sieve test",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xawyj9b4y3rq",
                        LanguageId = 2
                    },
                    new()
                    {
                        Name = "22. Testen mit Sieb", // TODO better german translation
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xawyj9b4y3rq",
                        LanguageId = 3
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    EformName = "22. Sigtetest",
                    Notifications = true,
                    RepeatEvery = 14, // 14
                    RepeatType = 1, // days
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 26,
                Type = AreaTypesEnum.Type1,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "99. Diverse",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5cyvenoqt2qk",
                        LanguageId = 1,
                    },
                    new()
                    {
                        Name = "99. Miscellaneous",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5cyvenoqt2qk",
                        LanguageId = 2, // en
                    },
                    new()
                    {
                        Name = "99. Sonstig",
                        Description = @"https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5cyvenoqt2qk",
                        LanguageId = 3,
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    ComplianceEnabled = true,
                },
            },
            new Area
            {
                Id = 23,
                Type = AreaTypesEnum.Type7,
                AreaTranslations = new List<AreaTranslation>
                {
                    new()
                    {
                        Name = "23. IE-indberetning", // todo need beter translate
                        Description = "https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz", // todo add link
                        LanguageId = 1, // da
                    },
                    new()
                    {
                        Name = "23. IE Reporting",
                        Description = "https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz", // todo add link
                        LanguageId = 2, // en
                    },
                    new()
                    {
                        Name = "23. IE-Berichterstattung", // todo need beter translate
                        Description = "https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz", // todo add link
                        LanguageId = 3, // ge
                    }
                },
                AreaInitialField = new AreaInitialField
                {
                    Notifications = true,
                    RepeatType = 1, // days
                    ComplianceEnabled = true,
                },
            }
        };

        public static List<AreaRule> AreaRules => new()
        {
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 1,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 1,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 2,
                IsDefault = true,
            },
            new()
            {
                EformName = "02. Sikkerhedsudstyr_værnemidler",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "02. Sikkerhedsudstyr_værnemidler" },
                    new() { LanguageId = 2, Name = "02. Safety equipment_protective equipment" },
                    new() { LanguageId = 3, Name = "02. Sicherheitsausrüstung_Schutzausrüstung" }
                },
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    RepeatEvery = 12, // 12
                    RepeatType = 3, // month
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 2,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 2,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 7,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 7,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 8,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 8,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 8,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 9,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 9,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = false,
                    ComplianceEnabled = true,
                },
                AreaId = 9,
                IsDefault = true,
            },
            /*new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 10,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 10,
                IsDefault = true,
            },
            new()
            {
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
                    ComplianceEnabled = true,
                },
                AreaId = 10,
                IsDefault = true,
            },
            new()
            {
                EformName = "11. Pillefyr",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "11. Pillefyr" },
                    new() { LanguageId = 2, Name = "11. Pellet stove" },
                    new() { LanguageId = 3, Name = "11. Pelletofen" }
                },
                AreaId = 11,
                IsDefault = true,
            },*/
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 12,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 12,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 12,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = false,
                    ComplianceEnabled = true,
                },
                AreaId = 12,
                IsDefault = true,
            },
            new()
            {
                EformName = "13. APV Medarbejder",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "13. APV Medarbejder" },
                    new() { LanguageId = 2, Name = "13. WPA Agriculture" },
                    new() { LanguageId = 3, Name = "13. Arbeitsplatz Landwirtschaft" }
                },
                AreaId = 13,
                IsDefault = true,
            },
            new()
            {
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
                    Notifications = true,
                    ComplianceEnabled = true,
                },
                AreaId = 21,
                IsDefault = true,
            },
        };

        public static List<AreaRule> AreaRulesForType7 => new()
        {
            new AreaRule
            {
                EformName = "23.00.01 Aflæsning vand",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.00.01 Aflæsning vand" }, // da
                    new() { LanguageId = 2, Name = "23.00.01 Water" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.00 Readings environmental management",
            },
            new AreaRule
            {
                EformName = "23.00.02 Aflæsning el",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.00.02 Aflæsning el" }, // da
                    new() { LanguageId = 2, Name = "23.00.02 Electricity" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.00 Readings environmental management",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.01 Fast overdækning gyllebeholder",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.01 Fast overdækning gyllebeholder" }, // da
                    new() { LanguageId = 2, Name = "23.01.01 Fixed cover slurry tank" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.02 Gyllekøling",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.02 Gyllekøling" }, // da
                    new() { LanguageId = 2, Name = "23.01.02 Slurry cooling" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.03 Forsuring",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.03 Forsuring" }, // da
                    new() { LanguageId = 2, Name = "23.01.03 Acidification" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.04 Ugentlig udslusning af gylle",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.04 Ugentlig udslusning af gylle" }, // da
                    new() { LanguageId = 2, Name = "23.01.04 Weekly slurry disposal of manure" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.05 Punktudsugning i slagtesvinestalde",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.05 Punktudsugning i slagtesvinestalde" }, // da
                    new() { LanguageId = 2, Name = "23.01.05 Point extraction in finisher barns" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.06 Varmevekslere til traditionelle slagtekyllingestalde",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new()
                    {
                        LanguageId = 1, Name = "23.01.06 Varmevekslere til traditionelle slagtekyllingestalde"
                    }, // da
                    new() { LanguageId = 2, Name = "23.01.06 Heat exchangers for traditional broilers" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.07 Gødningsbånd til æglæggende høns",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.07 Gødningsbånd til æglæggende høns" }, // da
                    new() { LanguageId = 2, Name = "23.01.07 Manure belt for laying hens" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.08 Biologisk luftrensning",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.08 Biologisk luftrensning" }, // da
                    new() { LanguageId = 2, Name = "23.01.08 Biological air purification" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.01.09 Kemisk luftrensning",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.01.09 Kemisk luftrensning" }, // da
                    new() { LanguageId = 2, Name = "23.01.09 Chemical air purification" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.01 Logbooks for any environmental technologies",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.01 Årlig visuel kontrol af gyllebeholdere",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.01 Årlig visuel kontrol af gyllebeholdere" }, // da
                    new() { LanguageId = 2, Name = "23.02.01 Annual visual inspection of slurry tanks" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.02 Gyllepumper mm.",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.02 Gyllepumper mm" }, // da
                    new() { LanguageId = 2, Name = "23.02.02 Slurry pumps etc." }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.03 Forsyningssystemer til vand og foder",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.03 Forsyningssystemer til vand og foder" }, // da
                    new() { LanguageId = 2, Name = "23.02.03 Water and feed supply systems" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.04 Varme-, køle- og ventilationssystemer",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.04 Varme-, køle- og ventilationssystemer" }, // da
                    new() { LanguageId = 2, Name = "23.02.04 Heating, cooling and ventilation systems" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv.",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new()
                    {
                        LanguageId = 1, Name = "23.02.05 Siloer og materiel i transportudstyr i forbindelse med foderanlæg - rør, snegle mv."
                    }, // da
                    new() { LanguageId = 2, Name = "23.02.05 Silos and equipment in transport equipment in connection with feeding systems (Pipes, augers, etc.)" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.06 Luftrensningssystemer",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.06 Luftrensningssystemer" }, // da
                    new() { LanguageId = 2, Name = "23.02.06 Air purification systems" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.07 Udstyr til drikkevand",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.07 Udstyr til drikkevand" }, // da
                    new() { LanguageId = 2, Name = "23.02.07 Equipment for drinking water" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.02.08 Maskiner til udbringning af husdyrgødning samt doseringsmekanisme" }, // da
                    new() { LanguageId = 2, Name = "23.02.08 Machines for application of livestock manure and dosing mechanism" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.02 Documentation of completed inspections",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.03.01 Miljøledelse",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.03.01 Miljøledelse" }, // da
                    new() { LanguageId = 2, Name = "23.03.01 Template Environmental Management" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.03 Documentation for environmental management",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.04.01 Fasefodring",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.04.01 Fasefodring" }, // da
                    new() { LanguageId = 2, Name = "23.04.01 Phase feeding" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.04 Compliance with feeding requirements",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.04.02 Reduceret indhold af råprotein",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.04.02 Reduceret indhold af råprotein" }, // da
                    new() { LanguageId = 2, Name = "23.04.02 Reduced content of crude protein" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.04 Compliance with feeding requirements",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
            new AreaRule
            {
                EformName = "23.04.03 Tilsætningsstoffer i foder - fytase eller andet",
                AreaRuleTranslations = new List<AreaRuleTranslation>
                {
                    new() { LanguageId = 1, Name = "23.04.03 Tilsætningsstoffer i foder - fytase eller andet" }, // da
                    new() { LanguageId = 2, Name = "23.04.03 Additives in feed (Phytase or other)" }, // en
                    new() { LanguageId = 3, Name = "" } // ge todo
                },
                AreaId = 23,
                IsDefault = true,
                FolderName = "23.04 Compliance with feeding requirements",
                AreaRuleInitialField = new AreaRuleInitialField
                {
                    ComplianceEnabled = true,
                },
            },
        };
    }
}