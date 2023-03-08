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
    using Microting.eForm.Infrastructure.Data.Entities;

    public static class BackendConfigurationSeedFolders
    {
        public static IEnumerable<Folder> NewFolders => new[]
        {
            new Folder
            {
                Id = 1,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 1, LanguageId = 1, Name = "00. Arbejdstid" },
                    new() {FolderId = 1, LanguageId = 2, Name = "00. Working hours" },
                    new() {FolderId = 1, LanguageId = 3, Name = "00. Arbeitszeit" },
                    new() {FolderId = 1, LanguageId = 4, Name = "00. Робочий час" }
                }
            },
            new Folder
            {
                Id = 2,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 2, LanguageId = 1, Name = "00.01 Opret opgave"},
                    new() {FolderId = 2, LanguageId = 2, Name = "00.01 Create task"},
                    new() {FolderId = 2, LanguageId = 3, Name = "00.01 Aufgabe erstellen"},
                    new() {FolderId = 2, LanguageId = 4, Name = "00.01 Створення завдання"}
                }
            },
            new Folder
            {
                Id = 3,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 3, LanguageId = 1, Name = "00.02 Huskelist"},
                    new() {FolderId = 3, LanguageId = 2, Name = "00.02 To-do-list"},
                    new() {FolderId = 3, LanguageId = 3, Name = "00.02 To-do-liste"},
                    new() {FolderId = 3, LanguageId = 4, Name = "00.02 Список справ"}
                }
            },
            new Folder
            {
                Id = 4,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 4, LanguageId = 1, Name = "01. Miljøstyring"},
                    new() {FolderId = 4, LanguageId = 2, Name = "01. Environmental Management"},
                    new() {FolderId = 4, LanguageId = 3, Name = "01. Umweltmanagement"},
                    new() {FolderId = 4, LanguageId = 4, Name = "01. Управління навколишнім середовищем"}
                }
            },
            new Folder
            {
                Id = 5,
                ParentId = 4,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 5, LanguageId = 1, Name = "Vand"},
                    new() {FolderId = 5, LanguageId = 2, Name = "Water"},
                    new() {FolderId = 5, LanguageId = 3, Name = "Wasser"},
                    new() {FolderId = 5, LanguageId = 4, Name = "Вода"}
                }
            },
            new Folder
            {
                Id = 6,
                ParentId = 4,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 6, LanguageId = 1, Name = "Elektricitet"},
                    new() {FolderId = 6, LanguageId = 2, Name = "Electricity"},
                    new() {FolderId = 6, LanguageId = 3, Name = "Elektrizität"},
                    new() {FolderId = 6, LanguageId = 4, Name = "Електрика"}
                }
            },
            new Folder
            {
                Id = 7,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 7, LanguageId = 1, Name = "02. Beredskab"},
                    new() {FolderId = 7, LanguageId = 2, Name = "02. Contingency"},
                    new() {FolderId = 7, LanguageId = 3, Name = "02. Kontingenz"},
                    new() {FolderId = 7, LanguageId = 4, Name = "02. Непередбачені обставини"}
                }
            },
            new Folder
            {
                Id = 8,
                ParentId = 7,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 8, LanguageId = 1, Name = "Beredskabsplan (PDF)"},
                    new() {FolderId = 8, LanguageId = 2, Name = "Contingency plan (PDF)"},
                    new() {FolderId = 8, LanguageId = 3, Name = "Notfallplan (PDF)"},
                    new() {FolderId = 8, LanguageId = 4, Name = "План дій у надзвичайних ситуаціях (PDF)"}
                }
            },
            new Folder
            {
                Id = 9,
                ParentId = 7,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 9, LanguageId = 1, Name = "Brandustyr"},
                    new() {FolderId = 9, LanguageId = 2, Name = "Fire equipment"},
                    new() {FolderId = 9, LanguageId = 3, Name = "Feuer-Ausrüstung"},
                    new() {FolderId = 9, LanguageId = 4, Name = "Пожежне обладнання"}
                }
            },
            new Folder
            {
                Id = 10,
                ParentId = 7,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 10, LanguageId = 1, Name = "Sikkerhedsudstyr"},
                    new() {FolderId = 10, LanguageId = 2, Name = "Safety equipment"},
                    new() {FolderId = 10, LanguageId = 3, Name = "Sicherheitsausrüstung"},
                    new() {FolderId = 10, LanguageId = 4, Name = "Обладнання для забезпечення безпеки"}
                }
            },
            new Folder
            {
                Id = 11,
                ParentId = 7,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 11, LanguageId = 1, Name = "Første hjælp"},
                    new() {FolderId = 11, LanguageId = 2, Name = "First aid"},
                    new() {FolderId = 11, LanguageId = 3, Name = "Erste Hilfe"},
                    new() {FolderId = 11, LanguageId = 4, Name = "Перша допомога"}
                }
            },
            new Folder
            {
                Id = 12,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 12, LanguageId = 1, Name = "03. Gylletanke"},
                    new() {FolderId = 12, LanguageId = 2, Name = "03. Slurry Tanks"},
                    new() {FolderId = 12, LanguageId = 3, Name = "03. Gülletanks"},
                    new() {FolderId = 12, LanguageId = 4, Name = "03. Цистерни для навозу"}
                }
            },
            new Folder
            {
                Id = 13,
                ParentId = 12,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 13, LanguageId = 1, Name = "SlurryTank 1 (Type = Open with alarm)"},
                    new()
                    {
                        FolderId = 13, LanguageId = 4,
                        Name = "Резервуар для гнойової рідини 1 (Тип = Відкритий з сигналізацією )"
                    }
                }
            },
            new Folder
            {
                Id = 14,
                ParentId = 13,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 14, LanguageId = 1, Name = "SlurryTank 1: Check floating Layer"},
                    new() {FolderId = 14, LanguageId = 4, Name = "Резервуар для гнойової рідини 1: Перевірте плаваючий шар"}
                }
            },
            new Folder
            {
                Id = 15,
                ParentId = 13,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 15, LanguageId = 1, Name = "SlurryTank 1: Check alarm"},
                    new() {FolderId = 15, LanguageId = 4, Name = "Резервуар для гнойової рідини 1: Перевірте сигналізацію"}
                }
            },
            new Folder
            {
                Id = 16,
                ParentId = 13,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 16, LanguageId = 1, Name = "SlurryTank 1: Check construction"},
                    new() {FolderId = 16, LanguageId = 4, Name = "Резервуар для гнойової рідини 1: Перевірте конструкцію"}
                }
            },
            new Folder
            {
                Id = 17,
                ParentId = 12,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 17, LanguageId = 1, Name = "SlurryTank 2 (Type = Open no alarm)"},
                    new()
                    {
                        FolderId = 17, LanguageId = 4,
                        Name = "Резервуар для гнойової рідини 2 (Тип = відкрито без сигналізації)"
                    }
                }
            },
            new Folder
            {
                Id = 18,
                ParentId = 17,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 18, LanguageId = 1, Name = "SlurryTank 2: Check floating Layer"},
                    new() {FolderId = 18, LanguageId = 4, Name = "Резервуар для гнойової рідини 2: Перевірте плаваючий шар"}
                }
            },
            new Folder
            {
                Id = 19,
                ParentId = 17,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 19, LanguageId = 1, Name = "SlurryTank 2: Check construction"},
                    new() {FolderId = 19, LanguageId = 4, Name = "Резервуар для гнойової рідини 2: Перевірте конструкцію"}
                }
            },
            new Folder
            {
                Id = 20,
                ParentId = 12,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 20, LanguageId = 1, Name = "SlurryTank 3 (Type = Closed with alarm)"},
                    new()
                    {
                        FolderId = 20, LanguageId = 4, Name = "Резервуар для гнойової рідини 3 (Тип = закритий з сигналізацією)"
                    }
                }
            },
            new Folder
            {
                Id = 21,
                ParentId = 20,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 21, LanguageId = 1, Name = "SlurryTank 3: Check alarm"},
                    new() {FolderId = 21, LanguageId = 4, Name = "Резервуар для гнойової рідини 3: Перевірте сигналізацію"}
                }
            },
            new Folder
            {
                Id = 22,
                ParentId = 20,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 22, LanguageId = 1, Name = "SlurryTank 3: Check construction"},
                    new() {FolderId = 22, LanguageId = 4, Name = "Резервуар для гнойової рідини 3: Перевірте конструкцію"}
                }
            },
            new Folder
            {
                Id = 23,
                ParentId = 12,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 23, LanguageId = 1, Name = "SlurryTank 4 (Type = Closed no alarm)"},
                    new()
                    {
                        FolderId = 23, LanguageId = 4,
                        Name = "Резервуар для гнойової рідини 4 (Тип = закритий без сигналізації)"
                    }
                }
            },
            new Folder
            {
                Id = 24,
                ParentId = 23,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 24, LanguageId = 1, Name = "SlurryTank 4: Check construction"},
                    new() {FolderId = 24, LanguageId = 4, Name = "Резервуар для гнойової рідини 4: Перевірте конструкцію"}
                }
            },
            new Folder
            {
                Id = 25,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 25, LanguageId = 1, Name = "04. Feeding documentation"},
                    new() {FolderId = 25, LanguageId = 2, Name = "04. Feeding documentation"},
                    new() {FolderId = 25, LanguageId = 3, Name = "04. Fütterungsdokumentation"},
                    new() {FolderId = 25, LanguageId = 4, Name = "04. Документація по годівлі"}
                }
            },
            new Folder
            {
                Id = 26,
                ParentId = 25,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 26, LanguageId = 1, Name = "FeedGroup 1"},
                    new() {FolderId = 26, LanguageId = 4, Name = "Група подачі 1"}
                }
            },
            new Folder
            {
                Id = 27,
                ParentId = 25,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 27, LanguageId = 1, Name = "FeedGroup 2"},
                    new() {FolderId = 27, LanguageId = 4, Name = "Група подачі 2"}
                }
            },
            new Folder
            {
                Id = 28,
                ParentId = 25,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 28, LanguageId = 1, Name = "FeedGroup 3"},
                    new() {FolderId = 28, LanguageId = 4, Name = "Група подачі 3"}
                }
            },
            new Folder
            {
                Id = 29,
                ParentId = 25,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 29, LanguageId = 1, Name = "FeedGroup N"},
                    new() {FolderId = 29, LanguageId = 4, Name = "Група подачі N"}
                }
            },
            new Folder
            {
                Id = 30,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 30, LanguageId = 1, Name = "05. Stalde"},
                    new() {FolderId = 30, LanguageId = 2, Name = "05. Stables"},
                    new() {FolderId = 30, LanguageId = 3, Name = "05. Stallungen"},
                    new() {FolderId = 30, LanguageId = 4, Name = "05. Штабельований"}
                }
            },
            new Folder
            {
                Id = 31,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 31, LanguageId = 1, Name = "06. Siloer"},
                    new() {FolderId = 31, LanguageId = 2, Name = "06. Silos"},
                    new() {FolderId = 31, LanguageId = 3, Name = "06. Silos"},
                    new() {FolderId = 31, LanguageId = 4, Name = "06. Бункер"}
                }
            },
            new Folder
            {
                Id = 32,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 32, LanguageId = 1, Name = "07. Skadedyrsbekæmpelse"},
                    new() {FolderId = 32, LanguageId = 2, Name = "07. Pest control"},
                    new() {FolderId = 32, LanguageId = 3, Name = "07. Schädlingsbekämpfung"},
                    new() {FolderId = 32, LanguageId = 4, Name = "07. Боротьба з шкідниками"}
                }
            },
            new Folder
            {
                Id = 33,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 33, LanguageId = 1, Name = "08. Miljøteknologier"},
                    new() {FolderId = 33, LanguageId = 2, Name = "08. Environmental Technologies"},
                    new() {FolderId = 33, LanguageId = 3, Name = "08. Umwelttechnologien"},
                    new() {FolderId = 33, LanguageId = 4, Name = "08. Екологічні технології"}
                }
            },
            new Folder
            {
                Id = 34,
                ParentId = 33,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 34, LanguageId = 1, Name = "Gyllekøling"},
                    new() {FolderId = 34, LanguageId = 2, Name = "Slurry cooling"},
                    new() {FolderId = 34, LanguageId = 3, Name = "Schlammkühlung"},
                    new() {FolderId = 34, LanguageId = 4, Name = "Охолодження гнойової рідини"}
                }
            },
            new Folder
            {
                Id = 35,
                ParentId = 33,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 35, LanguageId = 1, Name = "Luftrensning"},
                    new() {FolderId = 35, LanguageId = 2, Name = "Air Cleaning"},
                    new() {FolderId = 35, LanguageId = 3, Name = "Luftreinigung"},
                    new() {FolderId = 35, LanguageId = 4, Name = "Очищення повітря"}
                }
            },
            new Folder
            {
                Id = 36,
                ParentId = 33,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 36, LanguageId = 1, Name = "Ansäuerung"},
                    new() {FolderId = 36, LanguageId = 2, Name = "Acidification"},
                    new() {FolderId = 36, LanguageId = 3, Name = "Ansäuerung"},
                    new() {FolderId = 36, LanguageId = 4, Name = "Підкислення"}
                }
            },
            new Folder
            {
                Id = 37,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 37, LanguageId = 1, Name = "10. Varmepumper"},
                    new() {FolderId = 37, LanguageId = 2, Name = "10. Heat pumps"},
                    new() {FolderId = 37, LanguageId = 3, Name = "10. Wärmepumpen"},
                    new() {FolderId = 37, LanguageId = 4, Name = "10. Теплова помпа"}
                }
            },
            new Folder
            {
                Id = 38,
                ParentId = 37,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 38, LanguageId = 1, Name = "Heat pump 1"},
                    new() {FolderId = 38, LanguageId = 4, Name = "Тепловий насос 1"}
                }
            },
            new Folder
            {
                Id = 39,
                ParentId = 37,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 39, LanguageId = 1, Name = "Heat pump 2"},
                    new() {FolderId = 39, LanguageId = 4, Name = "Тепловий насос 2"}
                }
            },
            new Folder
            {
                Id = 40,
                ParentId = 37,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 40, LanguageId = 1, Name = "Heat pump N"},
                    new() {FolderId = 40, LanguageId = 4, Name = "Тепловий насос N"}
                }
            },
            new Folder
            {
                Id = 41,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 41, LanguageId = 1, Name = "11. Pilleovne"},
                    new() {FolderId = 41, LanguageId = 2, Name = "11. Pellot stoves"},
                    new() {FolderId = 41, LanguageId = 3, Name = "11. Pelletöfen"},
                    new() {FolderId = 41, LanguageId = 4, Name = "11. Печі-пелети"}
                }
            },
            new Folder
            {
                Id = 42,
                ParentId = 41,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 42, LanguageId = 1, Name = "Pellet stove 1"},
                    new() {FolderId = 42, LanguageId = 4, Name = "Пелетна Піч 1"}
                }
            },
            new Folder
            {
                Id = 43,
                ParentId = 41,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 43, LanguageId = 1, Name = "Pellet stove 2"},
                    new() {FolderId = 43, LanguageId = 4, Name = "Пелетна Піч 2"}
                }
            },
            new Folder
            {
                Id = 44,
                ParentId = 41,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 44, LanguageId = 1, Name = "Pellet stove N"},
                    new() {FolderId = 44, LanguageId = 4, Name = "Пелетна Піч N"}
                }
            },
            new Folder
            {
                Id = 45,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 45, LanguageId = 1, Name = "16. Miljøfarlige stoffer"},
                    new() {FolderId = 45, LanguageId = 2, Name = "16. Environmentally hazardous substances"},
                    new() {FolderId = 45, LanguageId = 3, Name = "16. Umweltgefährdende Stoffe"},
                    new() {FolderId = 45, LanguageId = 4, Name = "16. Речовини, небезпечні для навколишнього середовища"}
                }
            },
            new Folder
            {
                Id = 46,
                ParentId = 45,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 46, LanguageId = 1, Name = "Diesel tank"},
                    new() {FolderId = 46, LanguageId = 2, Name = "Diesel tank"},
                    new() {FolderId = 46, LanguageId = 3, Name = "Dieseltank"},
                    new() {FolderId = 46, LanguageId = 4, Name = "Дизельний бак"}
                }
            },
            new Folder
            {
                Id = 47,
                ParentId = 45,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 47, LanguageId = 1, Name = "Motor og spildolie"},
                    new() {FolderId = 47, LanguageId = 2, Name = "Engine and waste oil"},
                    new() {FolderId = 47, LanguageId = 3, Name = "Motor- und Altöl"},
                    new() {FolderId = 47, LanguageId = 4, Name = "Двигун і відпрацьоване масло"}
                }
            },
            new Folder
            {
                Id = 48,
                ParentId = 45,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 48, LanguageId = 1, Name = "Kemi"},
                    new() {FolderId = 48, LanguageId = 2, Name = "Chemistry"},
                    new() {FolderId = 48, LanguageId = 3, Name = "Chemie"},
                    new() {FolderId = 48, LanguageId = 4, Name = "Хімія"}
                }
            },
            new Folder
            {
                Id = 49,
                ParentId = 45,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 49, LanguageId = 1, Name = "Affald"},
                    new() {FolderId = 49, LanguageId = 2, Name = "Trash"},
                    new() {FolderId = 49, LanguageId = 3, Name = "Müll"},
                    new() {FolderId = 49, LanguageId = 4, Name = "Сміття"}
                }
            },
            new Folder
            {
                Id = 50,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 50, LanguageId = 1, Name = "19. Arbejdspladsvurdering"},
                    new() {FolderId = 50, LanguageId = 2, Name = "19. Work Place Assesment"},
                    new() {FolderId = 50, LanguageId = 3, Name = "19. Arbeitsplatzbewertung"},
                    new() {FolderId = 50, LanguageId = 4, Name = "19. Оцінка Робочого Місця"}
                }
            },
            new Folder
            {
                Id = 51,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 51, LanguageId = 1, Name = "20. Maskiner"},
                    new() {FolderId = 51, LanguageId = 2, Name = "20. Machines"},
                    new() {FolderId = 51, LanguageId = 3, Name = "20. Maschinen"},
                    new() {FolderId = 51, LanguageId = 4, Name = "20. Машини"}
                }
            },
            new Folder
            {
                Id = 52,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 52, LanguageId = 1, Name = "21. DANISH Standard"},
                    new() {FolderId = 52, LanguageId = 2, Name = "21. DANISH Standard"},
                    new() {FolderId = 52, LanguageId = 3, Name = "21. DÄNISCHER Standard"},
                    new() {FolderId = 52, LanguageId = 4, Name = "21. Датський стандарт"}
                }
            },
            new Folder
            {
                Id = 53,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 53, LanguageId = 1, Name = "24. Halebid"},
                    new() {FolderId = 53, LanguageId = 2, Name = "24. Tail bite"},
                    new() {FolderId = 53, LanguageId = 3, Name = "24. Schwanzbiss"},
                    new() {FolderId = 53, LanguageId = 4, Name = "24. Мовний укус"}
                }
            },
            new Folder
            {
                Id = 54,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 54, LanguageId = 1, Name = "26. Sigttest"},
                    new() {FolderId = 54, LanguageId = 2, Name = "26. Sieve test"},
                    new() {FolderId = 54, LanguageId = 3, Name = "26. Siebtest"},
                    new() {FolderId = 54, LanguageId = 4, Name = "26. Випробування на сито"}
                }
            },
            new Folder
            {
                Id = 55,
                ParentId = 54,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 55, LanguageId = 1, Name = "Feed group 1"},
                    new() {FolderId = 55, LanguageId = 4, Name = "Група кормів 1"}
                }
            },
            new Folder
            {
                Id = 56,
                ParentId = 54,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 56, LanguageId = 1, Name = "Feed group 2"},
                    new() {FolderId = 56, LanguageId = 4, Name = "Група кормів 2"}
                }
            },
            new Folder
            {
                Id = 57,
                ParentId = 54,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 57, LanguageId = 1, Name = "Feed group N"},
                    new() {FolderId = 57, LanguageId = 4, Name = "Група кормів N"}
                }
            },
            new Folder
            {
                Id = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 58, LanguageId = 1, Name = "27. Tilbagevendende opgaver (man-søn)"},
                    new() {FolderId = 58, LanguageId = 2, Name = "27. Recurring tasks (mon-sun)"},
                    new() {FolderId = 58, LanguageId = 3, Name = "27. Wiederkehrende Aufgaben (Mo-So)"},
                    new() {FolderId = 58, LanguageId = 4, Name = "27. Повторювані завдання (пн-нд)"}
                }
            },
            new Folder
            {
                Id = 59,
                ParentId = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 59, LanguageId = 1, Name = "Mandag"},
                    new() {FolderId = 59, LanguageId = 2, Name = "Monday"},
                    new() {FolderId = 59, LanguageId = 3, Name = "Montag"},
                    new() {FolderId = 59, LanguageId = 4, Name = "Понеділок"}
                }
            },
            new Folder
            {
                Id = 60,
                ParentId = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 60, LanguageId = 1, Name = "Tirsdag"},
                    new() {FolderId = 60, LanguageId = 2, Name = "Tuesday"},
                    new() {FolderId = 60, LanguageId = 3, Name = "Dienstag"},
                    new() {FolderId = 60, LanguageId = 4, Name = "Вівторок"}
                }
            },
            new Folder
            {
                Id = 61,
                ParentId = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 61, LanguageId = 1, Name = "Onsdag"},
                    new() {FolderId = 61, LanguageId = 2, Name = "Wednesday"},
                    new() {FolderId = 61, LanguageId = 3, Name = "Mittwoch"},
                    new() {FolderId = 61, LanguageId = 4, Name = "Середа"}
                }
            },
            new Folder
            {
                Id = 62,
                ParentId = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 62, LanguageId = 1, Name = "Torsdag"},
                    new() {FolderId = 62, LanguageId = 2, Name = "Thursday"},
                    new() {FolderId = 62, LanguageId = 3, Name = "Donnerstag"},
                    new() {FolderId = 62, LanguageId = 4, Name = "Четвер"}
                }
            },
            new Folder
            {
                Id = 63,
                ParentId = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 63, LanguageId = 1, Name = "Fredag"},
                    new() {FolderId = 63, LanguageId = 2, Name = "Friday"},
                    new() {FolderId = 63, LanguageId = 3, Name = "Freitag"},
                    new() {FolderId = 63, LanguageId = 4, Name = "П'ятниця"}
                }
            },
            new Folder
            {
                Id = 64,
                ParentId = 58,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 64, LanguageId = 1, Name = "Lørdag"},
                    new() {FolderId = 64, LanguageId = 2, Name = "Saturday"},
                    new() {FolderId = 64, LanguageId = 3, Name = "Samstag"},
                    new() {FolderId = 64, LanguageId = 4, Name = "Субота"}
                }
            },
            new Folder
            {
                Id = 65,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 65, LanguageId = 1, Name = "Søndag"},
                    new() {FolderId = 65, LanguageId = 2, Name = "Sunday"},
                    new() {FolderId = 65, LanguageId = 3, Name = "Sonntag"},
                    new() {FolderId = 65, LanguageId = 4, Name = "Неділя"}
                }
            },
            new Folder
            {
                Id = 66,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 66, LanguageId = 1, Name = "31. Alarm"},
                    new() {FolderId = 66, LanguageId = 2, Name = "31. Alarm"},
                    new() {FolderId = 66, LanguageId = 3, Name = "31. Alarm"},
                    new() {FolderId = 66, LanguageId = 4, Name = "31. Тривога"}
                }
            },
            new Folder
            {
                Id = 67,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 67, LanguageId = 1, Name = "33. Ventilation"},
                    new() {FolderId = 67, LanguageId = 2, Name = "33. Ventilation"},
                    new() {FolderId = 67, LanguageId = 3, Name = "33. Belüftung"},
                    new() {FolderId = 67, LanguageId = 4, Name = "33. Вентиляція"}
                }
            },
            new Folder
            {
                Id = 68,
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 68, LanguageId = 1, Name = "34. Eftersyn af elværktøj"},
                    new() {FolderId = 68, LanguageId = 2, Name = "34. Inspection of power tools"},
                    new() {FolderId = 68, LanguageId = 3, Name = "34. Inspektion von Elektrowerkzeugen"},
                    new() {FolderId = 68, LanguageId = 4, Name = "34. Перевірка електроінструментів"}
                }
            },
            new Folder
            {
                FolderTranslations = new List<FolderTranslation>
                {
                    new() {FolderId = 69, LanguageId = 1, Name = "35. Eftersyn af stiger"},
                    new() {FolderId = 69, LanguageId = 2, Name = "35. Inspection of ladders"},
                    new() {FolderId = 69, LanguageId = 3, Name = "35. Inspektion von Leitern"},
                    new() {FolderId = 69, LanguageId = 4, Name = "35. Огляд сходів"}
                }
            }
        };

    }
}
