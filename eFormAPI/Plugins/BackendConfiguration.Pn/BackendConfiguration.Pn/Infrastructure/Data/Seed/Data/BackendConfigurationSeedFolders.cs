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
        public static IEnumerable<Folder> SeedFolders => new[]
        {
            new Folder
            {
                Id = 1,
            },
            new Folder
            {
                Id = 2,
            },
            new Folder
            {
                Id = 3,
            },
            new Folder
            {
                Id = 4,
            },
            new Folder
            {
                Id = 5,
                ParentId = 4,
            },
            new Folder
            {
                Id = 6,
                ParentId = 4,
            },


            new Folder
            {
                Id = 7,
            },
            new Folder
            {
                Id = 8,
                ParentId = 7,
            },
            new Folder
            {
                Id = 9,
                ParentId = 7,
            },
            new Folder
            {
                Id = 10,
                ParentId = 7,
            },
            new Folder
            {
                Id = 11,
                ParentId = 7,
            },
            new Folder
            {
                Id = 12,
            },
            new Folder
            {
                Id = 13,
                ParentId = 12,
            },
            new Folder
            {
                Id = 14,
                ParentId = 13,
            },
            new Folder
            {
                Id = 15,
                ParentId = 13,
            },
            new Folder
            {
                Id = 16,
                ParentId = 13,
            },
            new Folder
            {
                Id = 17,
                ParentId = 12,
            },
            new Folder
            {
                Id = 18,
                ParentId = 17,
            },
            new Folder
            {
                Id = 19,
                ParentId = 17,
            },
            new Folder
            {
                Id = 20,
                ParentId = 12,
            },
            new Folder
            {
                Id = 21,
                ParentId = 20,
            },
            new Folder
            {
                Id = 22,
                ParentId = 20,
            },
            new Folder
            {
                Id = 23,
                ParentId = 12,
            },
            new Folder
            {
                Id = 24,
                ParentId = 23,
            },
            new Folder
            {
                Id = 25,
            },
            new Folder
            {
                Id = 26,
                ParentId = 25,
            },
            new Folder
            {
                Id = 27,
                ParentId = 25,
            },
            new Folder
            {
                Id = 28,
                ParentId = 25,
            },
            new Folder
            {
                Id = 29,
                ParentId = 25,
            },
            new Folder
            {
                Id = 30,
            },
            new Folder
            {
                Id = 31,
            },
            new Folder
            {
                Id = 32,
            },
            new Folder
            {
                Id = 33,
            },
            new Folder
            {
                Id = 34,
                ParentId = 33,
            },
            new Folder
            {
                Id = 35,
                ParentId = 33,
            },
            new Folder
            {
                Id = 36,
                ParentId = 33,
            },
            new Folder
            {
                Id = 37,
            },
            new Folder
            {
                Id = 38,
                ParentId = 37,
            },
            new Folder
            {
                Id = 39,
                ParentId = 37,
            },
            new Folder
            {
                Id = 40,
                ParentId = 37,
            },
            new Folder
            {
                Id = 41,
            },
            new Folder
            {
                Id = 42,
                ParentId = 41,
            },
            new Folder
            {
                Id = 43,
                ParentId = 41,
            },
            new Folder
            {
                Id = 44,
                ParentId = 41,
            },
            new Folder
            {
                Id = 45,
            },
            new Folder
            {
                Id = 46,
                ParentId = 45,
            },
            new Folder
            {
                Id = 47,
                ParentId = 45,
            },
            new Folder
            {
                Id = 48,
                ParentId = 45,
            },
            new Folder
            {
                Id = 49,
                ParentId = 45,
            },
            new Folder
            {
                Id = 50,
            },
            new Folder
            {
                Id = 51,
            },
            new Folder
            {
                Id = 52,
            },
            new Folder
            {
                Id = 53,
            },
            new Folder
            {
                Id = 54,
            },
            new Folder
            {
                Id = 55,
                ParentId = 54,
            },
            new Folder
            {
                Id = 56,
                ParentId = 54,
            },
            new Folder
            {
                Id = 57,
                ParentId = 54,
            },
            new Folder
            {
                Id = 58,
            },
            new Folder
            {
                Id = 59,
                ParentId = 58,
            },
            new Folder
            {
                Id = 60,
                ParentId = 58,
            },
            new Folder
            {
                Id = 61,
                ParentId = 58,
            },
            new Folder
            {
                Id = 62,
                ParentId = 58,
            },
            new Folder
            {
                Id = 62,
                ParentId = 58,
            },
            new Folder
            {
                Id = 63,
                ParentId = 58,
            },
            new Folder
            {
                Id = 64,
                ParentId = 58,
            },
            new Folder
            {
                Id = 65,
            },
            new Folder
            {
                Id = 66,
            },
            new Folder
            {
                Id = 67,
            },
            new Folder
            {
                Id = 68,
            },
            new Folder
            {
                Id = 69,
            },
        };

        public static IEnumerable<FolderTranslation> SeedFolderTranslations = new[]
        {
            new FolderTranslation {FolderId = 1, LanguageId = 1, Name = "00. Working hours",},
            new FolderTranslation {FolderId = 1, LanguageId = 4, Name = "00. Робочий час",},
            new FolderTranslation {FolderId = 2, LanguageId = 1, Name = "00.01 Create task"},
            new FolderTranslation {FolderId = 2, LanguageId = 4, Name = "00.01 Створення завдання"},
            new FolderTranslation {FolderId = 3, LanguageId = 1, Name = "00.02 To-do-list"},
            new FolderTranslation {FolderId = 3, LanguageId = 4, Name = "00.02 Список справ"},
            new FolderTranslation {FolderId = 4, LanguageId = 1, Name = "01. Environmental Management"},
            new FolderTranslation {FolderId = 4, LanguageId = 4, Name = "01. Управління навколишнім середовищем"},
            new FolderTranslation {FolderId = 5, LanguageId = 1, Name = "Water"},
            new FolderTranslation {FolderId = 5, LanguageId = 4, Name = "Вода"},
            new FolderTranslation {FolderId = 6, LanguageId = 1, Name = "Electricity"},
            new FolderTranslation {FolderId = 6, LanguageId = 4, Name = "Електрика"},
            new FolderTranslation {FolderId = 7, LanguageId = 1, Name = "02. Contingency"},
            new FolderTranslation {FolderId = 7, LanguageId = 4, Name = "02. Непередбачені обставини"},
            new FolderTranslation {FolderId = 8, LanguageId = 1, Name = "Contingency plan (PDF)"},
            new FolderTranslation {FolderId = 8, LanguageId = 4, Name = "План дій у надзвичайних ситуаціях (PDF)"},
            new FolderTranslation {FolderId = 9, LanguageId = 1, Name = "Fire equipment"},
            new FolderTranslation {FolderId = 9, LanguageId = 4, Name = "Пожежне обладнання"},
            new FolderTranslation {FolderId = 10, LanguageId = 1, Name = "Safety equipment"},
            new FolderTranslation {FolderId = 10, LanguageId = 4, Name = "Обладнання для забезпечення безпеки"},
            new FolderTranslation {FolderId = 11, LanguageId = 1, Name = "First aid"},
            new FolderTranslation {FolderId = 11, LanguageId = 4, Name = "Перша допомога"},
            new FolderTranslation {FolderId = 12, LanguageId = 1, Name = "03. Slurry tanks"},
            new FolderTranslation {FolderId = 12, LanguageId = 4, Name = "03. Резервуари для гнойової рідини"},
            new FolderTranslation {FolderId = 13, LanguageId = 1, Name = "SlurryTank 1 (Type = Open with alarm)"},
            new FolderTranslation
            {
                FolderId = 13, LanguageId = 4,
                Name = "Резервуар для гнойової рідини 1 (Тип = Відкритий з сигналізацією )"
            },
            new FolderTranslation {FolderId = 14, LanguageId = 1, Name = "SlurryTank 1: Check floating Layer"},
            new FolderTranslation {FolderId = 14, LanguageId = 4, Name = "Резервуар для гнойової рідини 1: Перевірте плаваючий шар"},
            new FolderTranslation {FolderId = 15, LanguageId = 1, Name = "SlurryTank 1: Check alarm"},
            new FolderTranslation {FolderId = 15, LanguageId = 4, Name = "Резервуар для гнойової рідини 1: Перевірте сигналізацію"},
            new FolderTranslation {FolderId = 16, LanguageId = 1, Name = "SlurryTank 1: Check construction"},
            new FolderTranslation {FolderId = 16, LanguageId = 4, Name = "Резервуар для гнойової рідини 1: Перевірте конструкцію"},
            new FolderTranslation {FolderId = 17, LanguageId = 1, Name = "SlurryTank 2 (Type = Open no alarm)"},
            new FolderTranslation
            {
                FolderId = 17, LanguageId = 4,
                Name = "Резервуар для гнойової рідини 2 (Тип = відкрито без сигналізації)"
            },
            new FolderTranslation {FolderId = 18, LanguageId = 1, Name = "SlurryTank 2: Check floating Layer"},
            new FolderTranslation {FolderId = 18, LanguageId = 4, Name = "Резервуар для гнойової рідини 2: Перевірте плаваючий шар"},
            new FolderTranslation {FolderId = 19, LanguageId = 1, Name = "SlurryTank 2: Check construction"},
            new FolderTranslation {FolderId = 19, LanguageId = 4, Name = "Резервуар для гнойової рідини 2: Перевірте конструкцію"},
            new FolderTranslation {FolderId = 20, LanguageId = 1, Name = "SlurryTank 3 (Type = Closed with alarm)"},
            new FolderTranslation
            {
                FolderId = 20, LanguageId = 4, Name = "Резервуар для гнойової рідини 3 (Тип = закритий з сигналізацією)"
            },
            new FolderTranslation {FolderId = 21, LanguageId = 1, Name = "SlurryTank 3: Check alarm"},
            new FolderTranslation {FolderId = 21, LanguageId = 4, Name = "Резервуар для гнойової рідини 3: Перевірте сигналізацію"},
            new FolderTranslation {FolderId = 22, LanguageId = 1, Name = "SlurryTank 3: Check construction"},
            new FolderTranslation {FolderId = 22, LanguageId = 4, Name = "Резервуар для гнойової рідини 3: Перевірте конструкцію"},
            new FolderTranslation {FolderId = 23, LanguageId = 1, Name = "SlurryTank 4 (Type = Closed no alarm)"},
            new FolderTranslation
            {
                FolderId = 23, LanguageId = 4,
                Name = "Резервуар для гнойової рідини 4 (Тип = закритий без сигналізації)"
            },
            new FolderTranslation {FolderId = 24, LanguageId = 1, Name = "SlurryTank 4: Check construction"},
            new FolderTranslation {FolderId = 24, LanguageId = 4, Name = "Резервуар для гнойової рідини 4: Перевірте конструкцію"},
            new FolderTranslation {FolderId = 25, LanguageId = 1, Name = "04. Feeding documentation"},
            new FolderTranslation {FolderId = 25, LanguageId = 4, Name = "04. Документація по годівлі"},
            new FolderTranslation {FolderId = 26, LanguageId = 1, Name = "FeedGroup 1"},
            new FolderTranslation {FolderId = 26, LanguageId = 4, Name = "Група подачі 1"},
            new FolderTranslation {FolderId = 27, LanguageId = 1, Name = "FeedGroup 2"},
            new FolderTranslation {FolderId = 27, LanguageId = 4, Name = "Група подачі 2"},
            new FolderTranslation {FolderId = 28, LanguageId = 1, Name = "FeedGroup 3"},
            new FolderTranslation {FolderId = 28, LanguageId = 4, Name = "Група подачі 3"},
            new FolderTranslation {FolderId = 29, LanguageId = 1, Name = "FeedGroup N"},
            new FolderTranslation {FolderId = 29, LanguageId = 4, Name = "Група подачі N"},
            new FolderTranslation {FolderId = 30, LanguageId = 1, Name = "05. Stables"},
            new FolderTranslation {FolderId = 30, LanguageId = 4, Name = "05. Штабельований"},
            new FolderTranslation {FolderId = 31, LanguageId = 1, Name = "06. Silos"},
            new FolderTranslation {FolderId = 31, LanguageId = 4, Name = "06. Бункер"},
            new FolderTranslation {FolderId = 32, LanguageId = 1, Name = "07. Pest control"},
            new FolderTranslation {FolderId = 32, LanguageId = 4, Name = "07. Боротьба з шкідниками"},
            new FolderTranslation {FolderId = 33, LanguageId = 1, Name = "08. Enviromental Technologies"},
            new FolderTranslation {FolderId = 33, LanguageId = 4, Name = "08. Екологічні технології"},
            new FolderTranslation {FolderId = 34, LanguageId = 1, Name = "Slurry cooling"},
            new FolderTranslation {FolderId = 34, LanguageId = 4, Name = "Охолодження гнойової рідини"},
            new FolderTranslation {FolderId = 35, LanguageId = 1, Name = "Air Cleaning"},
            new FolderTranslation {FolderId = 35, LanguageId = 4, Name = "Очищення повітря"},
            new FolderTranslation {FolderId = 36, LanguageId = 1, Name = "Acidification"},
            new FolderTranslation {FolderId = 36, LanguageId = 4, Name = "Підкислення"},
            new FolderTranslation {FolderId = 37, LanguageId = 1, Name = "10. Heat pumps"},
            new FolderTranslation {FolderId = 37, LanguageId = 4, Name = "10. Теплова помпа"},
            new FolderTranslation {FolderId = 38, LanguageId = 1, Name = "Heat pump 1"},
            new FolderTranslation {FolderId = 38, LanguageId = 4, Name = "Тепловий насос 1"},
            new FolderTranslation {FolderId = 39, LanguageId = 1, Name = "Heat pump 2"},
            new FolderTranslation {FolderId = 39, LanguageId = 4, Name = "Тепловий насос 2"},
            new FolderTranslation {FolderId = 40, LanguageId = 1, Name = "Heat pump N"},
            new FolderTranslation {FolderId = 40, LanguageId = 4, Name = "Тепловий насос N"},
            new FolderTranslation {FolderId = 41, LanguageId = 1, Name = "11. Pellot stoves"},
            new FolderTranslation {FolderId = 41, LanguageId = 4, Name = "11. Печі-пелети"},
            new FolderTranslation {FolderId = 42, LanguageId = 1, Name = "Pellet stove 1"},
            new FolderTranslation {FolderId = 42, LanguageId = 4, Name = "Пелетна Піч 1"},
            new FolderTranslation {FolderId = 43, LanguageId = 1, Name = "Pellet stove 2"},
            new FolderTranslation {FolderId = 43, LanguageId = 4, Name = "Пелетна Піч 2"},
            new FolderTranslation {FolderId = 44, LanguageId = 1, Name = "Pellet stove N"},
            new FolderTranslation {FolderId = 44, LanguageId = 4, Name = "Пелетна Піч N"},
            new FolderTranslation {FolderId = 45, LanguageId = 1, Name = "16. Environmentally hazardous substances"},
            new FolderTranslation {FolderId = 45, LanguageId = 4, Name = "16. Речовини, небезпечні для навколишнього середовища"},
            new FolderTranslation {FolderId = 46, LanguageId = 1, Name = "Diesel tank"},
            new FolderTranslation {FolderId = 46, LanguageId = 4, Name = "Дизельний бак"},
            new FolderTranslation {FolderId = 47, LanguageId = 1, Name = "Engine and waste oil"},
            new FolderTranslation {FolderId = 47, LanguageId = 4, Name = "Двигун і відпрацьоване масло"},
            new FolderTranslation {FolderId = 48, LanguageId = 1, Name = "Chemistry"},
            new FolderTranslation {FolderId = 48, LanguageId = 4, Name = "Хімія"},
            new FolderTranslation {FolderId = 49, LanguageId = 1, Name = "Trash"},
            new FolderTranslation {FolderId = 49, LanguageId = 4, Name = "Сміття"},
            new FolderTranslation {FolderId = 50, LanguageId = 1, Name = "19. Work Place Assesment"},
            new FolderTranslation {FolderId = 50, LanguageId = 4, Name = "19. Оцінка Робочого Місця"},
            new FolderTranslation {FolderId = 51, LanguageId = 1, Name = "20. Machines"},
            new FolderTranslation {FolderId = 51, LanguageId = 4, Name = "20. Машини"},
            new FolderTranslation {FolderId = 52, LanguageId = 1, Name = "21. DANISH Standard"},
            new FolderTranslation {FolderId = 52, LanguageId = 4, Name = "21. Датський стандарт"},
            new FolderTranslation {FolderId = 53, LanguageId = 1, Name = "24. Tale bite"},
            new FolderTranslation {FolderId = 53, LanguageId = 4, Name = "24. Мовний укус"},
            new FolderTranslation {FolderId = 54, LanguageId = 1, Name = "26. Sieve test"},
            new FolderTranslation {FolderId = 54, LanguageId = 4, Name = "26. Випробування на сито"},
            new FolderTranslation {FolderId = 55, LanguageId = 1, Name = "Feed group 1"},
            new FolderTranslation {FolderId = 55, LanguageId = 4, Name = "Група кормів 1"},
            new FolderTranslation {FolderId = 56, LanguageId = 1, Name = "Feed group 2"},
            new FolderTranslation {FolderId = 56, LanguageId = 4, Name = "Група кормів 2"},
            new FolderTranslation {FolderId = 57, LanguageId = 1, Name = "Feed group N"},
            new FolderTranslation {FolderId = 57, LanguageId = 4, Name = "Група кормів N"},
            new FolderTranslation {FolderId = 58, LanguageId = 1, Name = "27. Recurring tasks (mon-sun)"},
            new FolderTranslation {FolderId = 58, LanguageId = 4, Name = "27. Повторювані завдання (пн-нд)"},
            new FolderTranslation {FolderId = 59, LanguageId = 1, Name = "Monday"},
            new FolderTranslation {FolderId = 59, LanguageId = 4, Name = "Понеділок"},
            new FolderTranslation {FolderId = 60, LanguageId = 1, Name = "Tuesday"},
            new FolderTranslation {FolderId = 60, LanguageId = 4, Name = "Вівторок"},
            new FolderTranslation {FolderId = 61, LanguageId = 1, Name = "Wednesday"},
            new FolderTranslation {FolderId = 61, LanguageId = 4, Name = "Середа"},
            new FolderTranslation {FolderId = 62, LanguageId = 1, Name = "Thursday"},
            new FolderTranslation {FolderId = 62, LanguageId = 4, Name = "Четвер"},
            new FolderTranslation {FolderId = 63, LanguageId = 1, Name = "Friday"},
            new FolderTranslation {FolderId = 63, LanguageId = 4, Name = "П'ятниця"},
            new FolderTranslation {FolderId = 64, LanguageId = 1, Name = "Saturday"},
            new FolderTranslation {FolderId = 64, LanguageId = 4, Name = "Субота"},
            new FolderTranslation {FolderId = 65, LanguageId = 1, Name = "Sunday"},
            new FolderTranslation {FolderId = 65, LanguageId = 4, Name = "Неділя"},
            new FolderTranslation {FolderId = 66, LanguageId = 1, Name = "31. Alarm"},
            new FolderTranslation {FolderId = 66, LanguageId = 4, Name = "31. Тривога"},
            new FolderTranslation {FolderId = 67, LanguageId = 1, Name = "33. Ventilation"},
            new FolderTranslation {FolderId = 67, LanguageId = 4, Name = "33. Вентиляція"},
            new FolderTranslation {FolderId = 68, LanguageId = 1, Name = "34. Inspection of power tools"},
            new FolderTranslation {FolderId = 68, LanguageId = 4, Name = "34. Перевірка електроінструментів"},
            new FolderTranslation {FolderId = 69, LanguageId = 1, Name = "35. Inspection of ladders"},
            new FolderTranslation {FolderId = 69, LanguageId = 4, Name = "35. Огляд сходів"},
            new FolderTranslation {FolderId = 70, LanguageId = 1, Name = "36. Inspection of wagons"},
        };
    }
}
