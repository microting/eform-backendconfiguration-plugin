-- Seed data for 420_eform-angular-items-planning-plugin.
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
TRUNCATE TABLE `Languages`;
TRUNCATE TABLE `PlanningCaseSiteVersions`;
TRUNCATE TABLE `PlanningCaseSites`;
TRUNCATE TABLE `PlanningCaseVersions`;
TRUNCATE TABLE `PlanningCases`;
TRUNCATE TABLE `PlanningNameTranslation`;
TRUNCATE TABLE `PlanningNameTranslationVersions`;
TRUNCATE TABLE `PlanningSiteVersions`;
TRUNCATE TABLE `PlanningSites`;
TRUNCATE TABLE `PlanningTagVersions`;
TRUNCATE TABLE `PlanningTags`;
TRUNCATE TABLE `PlanningVersions`;
TRUNCATE TABLE `Plannings`;
TRUNCATE TABLE `PlanningsTags`;
TRUNCATE TABLE `PlanningsTagsVersions`;
TRUNCATE TABLE `PluginConfigurationValueVersions`;
TRUNCATE TABLE `PluginConfigurationValues`;
TRUNCATE TABLE `PluginGroupPermissionVersions`;
TRUNCATE TABLE `PluginGroupPermissions`;
TRUNCATE TABLE `PluginPermissions`;
TRUNCATE TABLE `UploadedDataVersions`;
TRUNCATE TABLE `UploadedDatas`;

/*M!999999\- enable the sandbox mode */ 

-- Identity counters the legacy dump carried in its CREATE TABLE clauses.
ALTER TABLE `Languages` AUTO_INCREMENT = 4;
ALTER TABLE `PluginConfigurationValues` AUTO_INCREMENT = 6;
ALTER TABLE `PluginGroupPermissionVersions` AUTO_INCREMENT = 5;
ALTER TABLE `PluginGroupPermissions` AUTO_INCREMENT = 5;
ALTER TABLE `PluginPermissions` AUTO_INCREMENT = 5;

SET FOREIGN_KEY_CHECKS = @OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS = @OLD_UNIQUE_CHECKS;
SET SQL_MODE = @OLD_SQL_MODE;
