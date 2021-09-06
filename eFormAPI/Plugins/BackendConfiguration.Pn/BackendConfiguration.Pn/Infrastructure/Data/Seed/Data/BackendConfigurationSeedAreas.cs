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
            },
            new Areas
            {
                Id = 2,
                Name = "Contingency",
            },
            new Areas
            {
                Id = 3,
                Name = "Slurry tanks",
            },
            new Areas
            {
                Id = 4,
                Name = "Feeding documentation (kun IE-husdyrbrug)",
            },
            new Areas
            {
                Id = 5,
                Name = "Stable preparations and tail bite documentation",
            },
            new Areas
            {
                Id = 6,
                Name = "Silos",
            },
            new Areas
            {
                Id = 7,
                Name = "Pest control",
            },
            new Areas
            {
                Id = 8,
                Name = "Aircleaning",
            },
            new Areas
            {
                Id = 9,
                Name = "Acidification",
            },
            new Areas
            {
                Id = 10,
                Name = "Heat pumps",
            },
            new Areas
            {
                Id = 11,
                Name = "Pellot stoves",
            },
            new Areas
            {
                Id = 12,
                Name = "Environmentally hazardous substances",
            },
            new Areas
            {
                Id = 13,
                Name = "Work Place Assesment",
            },
            new Areas
            {
                Id = 14,
                Name = "Machines",
            },
            new Areas
            {
                Id = 15,
                Name = "Inspection of power tools",
            },
            new Areas
            {
                Id = 16,
                Name = "Inspection of wagons",
            },
            new Areas
            {
                Id = 17,
                Name = "Inspection of ladders",
            },
            new Areas
            {
                Id = 18,
                Name = "Alarm",
            },
            new Areas
            {
                Id = 19,
                Name = "Ventilation",
            },
            new Areas
            {
                Id = 20,
                Name = "Recurring tasks (mon-sun)",
            },
            new Areas
            {
                Id = 21,
                Name = "DANISH Standard",
            },
            new Areas
            {
                Id = 22,
                Name = "Sieve test",
            },
            new Areas
            {
                Id = 23,
                Name = "Water consumption",
            },
            new Areas
            {
                Id = 24,
                Name = "Electricity consumption",
            },
            new Areas
            {
                Id = 25,
                Name = "Field irrigation consumption",
            },
            new Areas
            {
                Id = 26,
                Name = "Environmental Management",
            },
            new Areas
            {
                Id = 27,
                Name = "Feeding documentation",
            },
            new Areas
            {
                Id = 28,
                Name = "Stable preparations and tail bite",
            },
            new Areas
            {
                Id = 29,
                Name = "Slurry cooling",
            },
            new Areas
            {
                Id = 30,
                Name = "Miscellaneous",
            },
        };
    }
}
