-- Seed data for 420_eform-backend-configuration-plugin.
--
-- DATA ONLY: no CREATE TABLE, no __EFMigrationsHistory. The schema comes from
-- the entity model via Database.EnsureCreated() in TestBaseSetup, so a column
-- added to the base package appears here for free and can never be "missing"
-- from a hand-maintained CREATE TABLE again. Every INSERT names its columns,
-- so a new column also cannot break the arity of an existing VALUES row - and
-- SQL_MODE is relaxed below, so a new NOT NULL column without a default takes
-- the implicit default instead of rejecting the row.
--
-- Only the ADDED-column case is handled automatically. A column a base package
-- renames or removes still breaks the INSERTs below, and has to be fixed here.
--
-- The TRUNCATE list below IS the list of tables this fixture owns and resets.
-- To seed a new table, add it there and add an INSERT that names its columns.
--
-- The SET @OLD_... statements below are plain SQL, not the /*!40014 ... */
-- executable comments mysqldump wraps them in. MySqlConnector's parser reads a
-- bare @name as a parameter placeholder and would reject these, but
-- Microting.EntityFrameworkCore.MySql forces AllowUserVariables=True onto the
-- connection string, so they reach the server. Re-check that before changing
-- provider or connection string.

SET @OLD_UNIQUE_CHECKS = @@UNIQUE_CHECKS, UNIQUE_CHECKS = 0;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS = 0;
SET @OLD_SQL_MODE = @@SQL_MODE, SQL_MODE = 'NO_AUTO_VALUE_ON_ZERO';

-- Empty every table this fixture owns, so re-running the seed is idempotent
-- (ResetDatabasePerTest fixtures replay it before each test).
TRUNCATE TABLE `AreaInitialFieldVersions`;
TRUNCATE TABLE `AreaInitialFields`;
TRUNCATE TABLE `AreaProperties`;
TRUNCATE TABLE `AreaPropertyVersions`;
TRUNCATE TABLE `AreaRuleInitialFields`;
TRUNCATE TABLE `AreaRulePlanningFiles`;
TRUNCATE TABLE `AreaRulePlanningFileVersions`;
TRUNCATE TABLE `GoogleOAuthTokens`;
TRUNCATE TABLE `GoogleOAuthTokenVersions`;
TRUNCATE TABLE `DriveWatchChannels`;
TRUNCATE TABLE `DriveWatchChannelVersions`;
TRUNCATE TABLE `AreaRulePlanningTagVersion`;
TRUNCATE TABLE `AreaRulePlanningTags`;
TRUNCATE TABLE `AreaRulePlannings`;
TRUNCATE TABLE `AreaRuleTranslationVersions`;
TRUNCATE TABLE `AreaRuleTranslations`;
TRUNCATE TABLE `AreaRuleVersions`;
TRUNCATE TABLE `AreaRules`;
TRUNCATE TABLE `AreaRulesPlanningVersions`;
TRUNCATE TABLE `AreaTranslationVersions`;
TRUNCATE TABLE `AreaTranslations`;
TRUNCATE TABLE `AreaVersions`;
TRUNCATE TABLE `Areas`;
TRUNCATE TABLE `ChemicalProductPropertieSites`;
TRUNCATE TABLE `ChemicalProductProperties`;
TRUNCATE TABLE `ChemicalProductPropertyVersionSites`;
TRUNCATE TABLE `ChemicalProductPropertyVersions`;
TRUNCATE TABLE `ComplianceVersions`;
TRUNCATE TABLE `Compliances`;
TRUNCATE TABLE `EmailAttachmentVersions`;
TRUNCATE TABLE `EmailAttachments`;
TRUNCATE TABLE `EmailVersions`;
TRUNCATE TABLE `Emails`;
TRUNCATE TABLE `FileTagVersions`;
TRUNCATE TABLE `FileTags`;
TRUNCATE TABLE `FileVersions`;
TRUNCATE TABLE `Files`;
TRUNCATE TABLE `FilesTags`;
TRUNCATE TABLE `FilesTagsVersions`;
TRUNCATE TABLE `PlanningSites`;
TRUNCATE TABLE `PlanningSitesVersions`;
TRUNCATE TABLE `PluginConfigurationValueVersions`;
TRUNCATE TABLE `PluginConfigurationValues`;
TRUNCATE TABLE `PluginGroupPermissionVersions`;
TRUNCATE TABLE `PluginGroupPermissions`;
TRUNCATE TABLE `PluginPermissions`;
TRUNCATE TABLE `PoolAccidentVersions`;
TRUNCATE TABLE `PoolAccidents`;
TRUNCATE TABLE `PoolHistorySiteVersions`;
TRUNCATE TABLE `PoolHistorySites`;
TRUNCATE TABLE `PoolHourResultVersions`;
TRUNCATE TABLE `PoolHourResults`;
TRUNCATE TABLE `PoolHourVersions`;
TRUNCATE TABLE `PoolHours`;
TRUNCATE TABLE `PropertieVersions`;
TRUNCATE TABLE `Properties`;
TRUNCATE TABLE `PropertyFileVersions`;
TRUNCATE TABLE `PropertyFiles`;
TRUNCATE TABLE `PropertySelectedLanguageVersions`;
TRUNCATE TABLE `PropertySelectedLanguages`;
TRUNCATE TABLE `PropertyWorkerVersions`;
TRUNCATE TABLE `PropertyWorkers`;
TRUNCATE TABLE `ProperyAreaFolderVersions`;
TRUNCATE TABLE `ProperyAreaFolders`;
TRUNCATE TABLE `TaskTrackerColumnVersions`;
TRUNCATE TABLE `TaskTrackerColumns`;
TRUNCATE TABLE `UploadedDataVersions`;
TRUNCATE TABLE `UploadedDatas`;
TRUNCATE TABLE `WorkorderCaseImageVersions`;
TRUNCATE TABLE `WorkorderCaseImages`;
TRUNCATE TABLE `WorkorderCaseVersions`;
TRUNCATE TABLE `WorkorderCases`;

/*M!999999\- enable the sandbox mode */ 
INSERT INTO `AreaInitialFields` (`Id`, `EformName`, `Notifications`, `RepeatEvery`, `RepeatType`, `DayOfWeek`, `Type`, `Alarm`, `EndDate`, `AreaId`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`, `ComplianceEnabled`) VALUES (1,'03. Kontrol konstruktion',1,12,3,NULL,2,2,NULL,3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),
(2,'05. Stald_klargøring',1,0,NULL,NULL,NULL,NULL,NULL,5,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),
(3,'',1,12,3,1,NULL,NULL,NULL,31,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1);
INSERT INTO `AreaTranslations` (`Id`, `AreaId`, `Name`, `Description`, `LanguageId`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`, `InfoBox`, `Placeholder`, `NewItemName`) VALUES (1,3,'03. Flydelag','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En gyllebeholder pr. linje','Gyllebeholder','Ny flydelag'),
(2,3,'03. Floating layer','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One slurry tank per line','Slurry tank','New floating layer'),
(3,3,'03. Schwimmende Ebene','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Gülle-Tank pro Zeile','Gülle-Tank','Neue Schwimmende Ebene'),
(4,5,'05. Halebid','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En stald pr. linje','Stald','Ny stald til klargøring'),
(5,5,'05. Tail bite','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One stable per line','Stable','New stable'),
(6,5,'05. Schwanzbiss','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Ställe pro Zeile','Ställe','Neue Ställe'),
(7,31,'00. Logbøger','',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et fokusområde pr. linje','Fokusområde','Nyt fokusområde'),
(8,31,'01. Log books','',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'An area of focus per line','Area of focus','New area of focus'),
(9,31,'01. Logbücher','',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ein Fokusområde pro Zeile','Fokusbereich','Neues Fokusområde');
INSERT INTO `AreaVersions` (`Id`, `Type`, `ItemPlanningTagId`, `AreaId`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`, `IsFarm`, `IsDisabled`) VALUES (1,2,0,3,'2024-06-13 09:57:37.678371','2024-06-13 09:57:37.678373','created',0,0,1,1,0),
(2,3,0,5,'2024-06-13 09:57:38.078308','2024-06-13 09:57:38.078310','created',0,0,1,1,0),
(3,1,0,31,'2024-06-13 09:57:38.847612','2024-06-13 09:57:38.847614','created',0,0,1,1,0);
INSERT INTO `Areas` (`Id`, `Type`, `ItemPlanningTagId`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`, `IsFarm`, `IsDisabled`) VALUES (3,2,0,'2024-06-13 09:57:37.678371','2024-06-13 09:57:37.678373','created',0,0,1,1,0),
(5,3,0,'2024-06-13 09:57:38.078308','2024-06-13 09:57:38.078310','created',0,0,1,1,0),
(31,1,0,'2024-06-13 09:57:38.847612','2024-06-13 09:57:38.847614','created',0,0,1,1,0);
INSERT INTO `PluginConfigurationValues` (`Id`, `Name`, `Value`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`) VALUES (1,'BackendConfigurationSettings:ReportSubHeaderName','','2024-06-13 09:56:33.342157','2024-06-13 09:56:33.342159','created',1,0,1),
(2,'BackendConfigurationSettings:ReportHeaderName','','2024-06-13 09:56:33.368565','2024-06-13 09:56:33.368567','created',1,0,1),
(3,'BackendConfigurationSettings:MaxChrNumbers','1000','2024-06-13 09:56:33.372442','2024-06-13 09:56:33.372443','created',1,0,1),
(4,'BackendConfigurationSettings:MaxCvrNumbers','1000','2024-06-13 09:56:33.375592','2024-06-13 09:56:33.375594','created',1,0,1);
INSERT INTO `PluginGroupPermissionVersions` (`Id`, `GroupId`, `PermissionId`, `IsEnabled`, `PluginGroupPermissionId`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`) VALUES (1,1,1,1,1,'2024-06-13 09:56:33.994609','2024-06-13 09:56:33.994611','created',0,0,1),
(2,1,5,1,2,'2024-06-13 09:56:34.491161','2024-06-13 09:56:34.491163','created',0,0,1),
(3,1,6,1,3,'2024-06-13 09:56:34.612039','2024-06-13 09:56:34.612041','created',0,0,1),
(4,1,2,1,4,'2024-06-13 09:56:34.751694','2024-06-13 09:56:34.751696','created',0,0,1),
(5,1,3,1,5,'2024-06-13 09:56:34.812371','2024-06-13 09:56:34.812373','created',0,0,1),
(6,1,4,1,6,'2024-06-13 09:56:34.993958','2024-06-13 09:56:34.993961','created',0,0,1),
(7,1,7,1,7,'2024-06-13 09:56:35.140614','2024-06-13 09:56:35.140615','created',0,0,1),
(8,1,8,1,8,'2024-06-13 09:56:35.240748','2024-06-13 09:56:35.240750','created',0,0,1);
INSERT INTO `PluginGroupPermissions` (`Id`, `GroupId`, `PermissionId`, `IsEnabled`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`) VALUES (1,1,1,1,'2024-06-13 09:56:33.994609','2024-06-13 09:56:33.994611','created',0,0,1),
(2,1,5,1,'2024-06-13 09:56:34.491161','2024-06-13 09:56:34.491163','created',0,0,1),
(3,1,6,1,'2024-06-13 09:56:34.612039','2024-06-13 09:56:34.612041','created',0,0,1),
(4,1,2,1,'2024-06-13 09:56:34.751694','2024-06-13 09:56:34.751696','created',0,0,1),
(5,1,3,1,'2024-06-13 09:56:34.812371','2024-06-13 09:56:34.812373','created',0,0,1),
(6,1,4,1,'2024-06-13 09:56:34.993958','2024-06-13 09:56:34.993961','created',0,0,1),
(7,1,7,1,'2024-06-13 09:56:35.140614','2024-06-13 09:56:35.140615','created',0,0,1),
(8,1,8,1,'2024-06-13 09:56:35.240748','2024-06-13 09:56:35.240750','created',0,0,1);
INSERT INTO `PluginPermissions` (`Id`, `PermissionName`, `ClaimName`, `CreatedAt`, `UpdatedAt`, `WorkflowState`, `CreatedByUserId`, `UpdatedByUserId`, `Version`) VALUES (1,'Access BackendConfiguration Plugin','backend_configuration_plugin_access','2024-06-13 09:56:33.382817',NULL,'created',1,0,1),
(2,'Create property','properties_create','2024-06-13 09:56:33.398168',NULL,'created',1,0,1),
(3,'Get properties','properties_get','2024-06-13 09:56:33.400505',NULL,'created',1,0,1),
(4,'Edit property','property_edit','2024-06-13 09:56:33.403103',NULL,'created',1,0,1),
(5,'Enable chemical management','chemical_management_enable','2024-06-13 09:56:33.404446',NULL,'created',1,0,1),
(6,'Enable document management','document_management_enable','2024-06-13 09:56:33.405668',NULL,'created',1,0,1),
(7,'Enable task management','task_management_enable','2024-06-13 09:56:33.406831',NULL,'created',1,0,1),
(8,'Enable time registration','time_registration_enable','2024-06-13 09:56:33.407895',NULL,'created',1,0,1);

-- Identity counters the legacy dump carried in its CREATE TABLE clauses.
ALTER TABLE `AreaInitialFields` AUTO_INCREMENT = 4;
ALTER TABLE `AreaTranslations` AUTO_INCREMENT = 10;
ALTER TABLE `AreaVersions` AUTO_INCREMENT = 4;
ALTER TABLE `Areas` AUTO_INCREMENT = 32;
ALTER TABLE `PluginConfigurationValues` AUTO_INCREMENT = 5;
ALTER TABLE `PluginGroupPermissionVersions` AUTO_INCREMENT = 9;
ALTER TABLE `PluginGroupPermissions` AUTO_INCREMENT = 9;
ALTER TABLE `PluginPermissions` AUTO_INCREMENT = 9;

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS = @OLD_UNIQUE_CHECKS;
SET SQL_MODE = @OLD_SQL_MODE;
