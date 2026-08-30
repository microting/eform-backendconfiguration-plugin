global using NUnit.Framework;
// #1122 — every calendar-service construction in this project passes these
// two collaborators; global so 25 fixtures do not each need the using.
global using BackendConfiguration.Pn.Services.CalendarOccurrenceRetraction;
global using BackendConfiguration.Pn.Services.CalendarPastSeriesBackfill;
