-- Seed data for 420_eform-angular-case-template-plugin.
--
-- DATA ONLY: no CREATE TABLE, no __EFMigrationsHistory. The schema comes from
-- the entity model via Database.EnsureCreated() in TestBaseSetup, so a column
-- added to the base package appears here for free and can never be "missing"
-- from a hand-maintained CREATE TABLE again. Every INSERT names its columns,
-- so a new column also cannot break the arity of an existing VALUES row - and
-- SQL_MODE is relaxed below, so a new NOT NULL column without a default takes
-- the implicit default instead of rejecting the row.
--
-- The TRUNCATE list below IS the list of tables this fixture owns and resets.
-- To seed a new table, add it there and add an INSERT that names its columns.

SET @OLD_UNIQUE_CHECKS = @@UNIQUE_CHECKS, UNIQUE_CHECKS = 0;
SET @OLD_FOREIGN_KEY_CHECKS = @@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS = 0;
SET @OLD_SQL_MODE = @@SQL_MODE, SQL_MODE = 'NO_AUTO_VALUE_ON_ZERO';

-- Empty every table this fixture owns, so re-running the seed is idempotent
-- (ResetDatabasePerTest fixtures replay it before each test).
TRUNCATE TABLE `CaseVersions`;
TRUNCATE TABLE `Cases`;
TRUNCATE TABLE `DocumentProperties`;
TRUNCATE TABLE `DocumentPropertyVersions`;
TRUNCATE TABLE `DocumentSiteTagVersions`;
TRUNCATE TABLE `DocumentSiteTags`;
TRUNCATE TABLE `DocumentSiteVersions`;
TRUNCATE TABLE `DocumentSites`;
TRUNCATE TABLE `DocumentTranslationVersions`;
TRUNCATE TABLE `DocumentTranslations`;
TRUNCATE TABLE `DocumentUploadedDataVersions`;
TRUNCATE TABLE `DocumentUploadedDatas`;
TRUNCATE TABLE `DocumentVersions`;
TRUNCATE TABLE `Documents`;
TRUNCATE TABLE `FolderProperties`;
TRUNCATE TABLE `FolderPropertyVersions`;
TRUNCATE TABLE `FolderTranslationVersions`;
TRUNCATE TABLE `FolderTranslations`;
TRUNCATE TABLE `FolderVersions`;
TRUNCATE TABLE `Folders`;
TRUNCATE TABLE `PluginConfigurationValueVersions`;
TRUNCATE TABLE `PluginConfigurationValues`;
TRUNCATE TABLE `PluginGroupPermissionVersions`;
TRUNCATE TABLE `PluginGroupPermissions`;
TRUNCATE TABLE `PluginPermissions`;

/*M!999999\- enable the sandbox mode */ 

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS = @OLD_UNIQUE_CHECKS;
SET SQL_MODE = @OLD_SQL_MODE;
