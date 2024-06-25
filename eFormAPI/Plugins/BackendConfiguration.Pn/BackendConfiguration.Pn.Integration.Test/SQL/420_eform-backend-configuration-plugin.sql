-- MariaDB dump 10.19  Distrib 10.6.16-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: 127.0.0.1    Database: 420_eform-backend-configuration-plugin
-- ------------------------------------------------------
-- Server version	10.8.8-MariaDB-1:10.8.8+maria~ubu2204

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

--
-- Table structure for table `AreaInitialFieldVersions`
--

DROP TABLE IF EXISTS `AreaInitialFieldVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaInitialFieldVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `EformName` longtext DEFAULT NULL,
  `Notifications` tinyint(1) NOT NULL,
  `RepeatEvery` int(11) DEFAULT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `DayOfWeek` int(11) DEFAULT NULL,
  `Type` int(11) DEFAULT NULL,
  `Alarm` int(11) DEFAULT NULL,
  `EndDate` datetime(6) DEFAULT NULL,
  `AreaId` int(11) NOT NULL,
  `AreaInitialFieldId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `ComplianceEnabled` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaInitialFieldVersions_AreaId` (`AreaId`),
  CONSTRAINT `FK_AreaInitialFieldVersions_Areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `Areas` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaInitialFieldVersions`
--

LOCK TABLES `AreaInitialFieldVersions` WRITE;
/*!40000 ALTER TABLE `AreaInitialFieldVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaInitialFieldVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaInitialFields`
--

DROP TABLE IF EXISTS `AreaInitialFields`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaInitialFields` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `EformName` longtext DEFAULT NULL,
  `Notifications` tinyint(1) NOT NULL,
  `RepeatEvery` int(11) DEFAULT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `DayOfWeek` int(11) DEFAULT NULL,
  `Type` int(11) DEFAULT NULL,
  `Alarm` int(11) DEFAULT NULL,
  `EndDate` datetime(6) DEFAULT NULL,
  `AreaId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `ComplianceEnabled` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_AreaInitialFields_AreaId` (`AreaId`),
  CONSTRAINT `FK_AreaInitialFields_Areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `Areas` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaInitialFields`
--

LOCK TABLES `AreaInitialFields` WRITE;
/*!40000 ALTER TABLE `AreaInitialFields` DISABLE KEYS */;
INSERT INTO `AreaInitialFields` VALUES (1,'03. Kontrol konstruktion',1,12,3,NULL,2,2,NULL,3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(2,'05. Stald_klargøring',1,0,NULL,NULL,NULL,NULL,NULL,5,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(3,'',1,12,3,1,NULL,NULL,NULL,31,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1);
/*!40000 ALTER TABLE `AreaInitialFields` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaProperties`
--

DROP TABLE IF EXISTS `AreaProperties`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaProperties` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyId` int(11) NOT NULL,
  `AreaId` int(11) NOT NULL,
  `Checked` tinyint(1) NOT NULL,
  `GroupMicrotingUuid` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaProperties_AreaId` (`AreaId`),
  KEY `IX_AreaProperties_PropertyId` (`PropertyId`),
  CONSTRAINT `FK_AreaProperties_Areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `Areas` (`Id`),
  CONSTRAINT `FK_AreaProperties_Properties_PropertyId` FOREIGN KEY (`PropertyId`) REFERENCES `Properties` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaProperties`
--

LOCK TABLES `AreaProperties` WRITE;
/*!40000 ALTER TABLE `AreaProperties` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaProperties` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaPropertyVersions`
--

DROP TABLE IF EXISTS `AreaPropertyVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaPropertyVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaPropertyId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `AreaId` int(11) NOT NULL,
  `Checked` tinyint(1) NOT NULL,
  `GroupMicrotingUuid` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaPropertyVersions`
--

LOCK TABLES `AreaPropertyVersions` WRITE;
/*!40000 ALTER TABLE `AreaPropertyVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaPropertyVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRuleInitialFields`
--

DROP TABLE IF EXISTS `AreaRuleInitialFields`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRuleInitialFields` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `EformName` longtext DEFAULT NULL,
  `Notifications` tinyint(1) NOT NULL,
  `RepeatEvery` int(11) DEFAULT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `DayOfWeek` int(11) DEFAULT NULL,
  `Type` int(11) DEFAULT NULL,
  `Alarm` int(11) DEFAULT NULL,
  `EndDate` datetime(6) DEFAULT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `ComplianceEnabled` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_AreaRuleInitialFields_AreaRuleId` (`AreaRuleId`),
  CONSTRAINT `FK_AreaRuleInitialFields_AreaRules_AreaRuleId` FOREIGN KEY (`AreaRuleId`) REFERENCES `AreaRules` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRuleInitialFields`
--

LOCK TABLES `AreaRuleInitialFields` WRITE;
/*!40000 ALTER TABLE `AreaRuleInitialFields` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRuleInitialFields` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRulePlanningTagVersion`
--

DROP TABLE IF EXISTS `AreaRulePlanningTagVersion`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRulePlanningTagVersion` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRulePlanningTagId` int(11) NOT NULL,
  `AreaRulePlanningId` int(11) NOT NULL,
  `ItemPlanningTagId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRulePlanningTagVersion`
--

LOCK TABLES `AreaRulePlanningTagVersion` WRITE;
/*!40000 ALTER TABLE `AreaRulePlanningTagVersion` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRulePlanningTagVersion` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRulePlanningTags`
--

DROP TABLE IF EXISTS `AreaRulePlanningTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRulePlanningTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRulePlanningId` int(11) NOT NULL,
  `ItemPlanningTagId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaRulePlanningTags_AreaRulePlanningId` (`AreaRulePlanningId`),
  CONSTRAINT `FK_AreaRulePlanningTags_AreaRulePlannings_AreaRulePlanningId` FOREIGN KEY (`AreaRulePlanningId`) REFERENCES `AreaRulePlannings` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRulePlanningTags`
--

LOCK TABLES `AreaRulePlanningTags` WRITE;
/*!40000 ALTER TABLE `AreaRulePlanningTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRulePlanningTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRulePlannings`
--

DROP TABLE IF EXISTS `AreaRulePlannings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRulePlannings` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `StartDate` datetime(6) DEFAULT NULL,
  `EndDate` datetime(6) DEFAULT NULL,
  `DayOfWeek` int(11) NOT NULL,
  `DayOfMonth` int(11) NOT NULL,
  `RepeatEvery` int(11) DEFAULT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `Status` tinyint(1) NOT NULL,
  `SendNotifications` tinyint(1) NOT NULL,
  `Alarm` int(11) NOT NULL,
  `Type` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `ItemPlanningId` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `HoursAndEnergyEnabled` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL DEFAULT 0,
  `AreaId` int(11) NOT NULL DEFAULT 0,
  `ComplianceEnabled` tinyint(1) NOT NULL DEFAULT 1,
  `UseStartDateAsStartOfPeriod` tinyint(1) NOT NULL DEFAULT 0,
  `ItemPlanningTagId` int(11) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaRulePlannings_AreaRuleId` (`AreaRuleId`),
  CONSTRAINT `FK_AreaRulePlannings_AreaRules_AreaRuleId` FOREIGN KEY (`AreaRuleId`) REFERENCES `AreaRules` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRulePlannings`
--

LOCK TABLES `AreaRulePlannings` WRITE;
/*!40000 ALTER TABLE `AreaRulePlannings` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRulePlannings` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRuleTranslationVersions`
--

DROP TABLE IF EXISTS `AreaRuleTranslationVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRuleTranslationVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleTranslationId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRuleTranslationVersions`
--

LOCK TABLES `AreaRuleTranslationVersions` WRITE;
/*!40000 ALTER TABLE `AreaRuleTranslationVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRuleTranslationVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRuleTranslations`
--

DROP TABLE IF EXISTS `AreaRuleTranslations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRuleTranslations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` varchar(250) DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaRuleTranslations_AreaRuleId` (`AreaRuleId`),
  CONSTRAINT `FK_AreaRuleTranslations_AreaRules_AreaRuleId` FOREIGN KEY (`AreaRuleId`) REFERENCES `AreaRules` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRuleTranslations`
--

LOCK TABLES `AreaRuleTranslations` WRITE;
/*!40000 ALTER TABLE `AreaRuleTranslations` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRuleTranslations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRuleVersions`
--

DROP TABLE IF EXISTS `AreaRuleVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRuleVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleId` int(11) NOT NULL,
  `AreaId` int(11) NOT NULL,
  `EformId` int(11) DEFAULT NULL,
  `EformName` longtext DEFAULT NULL,
  `FolderId` int(11) NOT NULL,
  `FolderName` longtext DEFAULT NULL,
  `Alarm` int(11) DEFAULT NULL,
  `Type` int(11) DEFAULT NULL,
  `ChecklistStable` tinyint(1) DEFAULT NULL,
  `TailBite` tinyint(1) DEFAULT NULL,
  `DayOfWeek` int(11) NOT NULL,
  `GroupItemId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `IsDefault` tinyint(1) NOT NULL,
  `RepeatEvery` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `ComplianceEnabled` tinyint(1) DEFAULT NULL,
  `ComplianceModifiable` tinyint(1) NOT NULL DEFAULT 0,
  `Notifications` tinyint(1) DEFAULT NULL,
  `NotificationsModifiable` tinyint(1) NOT NULL DEFAULT 0,
  `SecondaryeFormId` int(11) NOT NULL DEFAULT 0,
  `SecondaryeFormName` longtext DEFAULT NULL,
  `CreatedInGuide` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRuleVersions`
--

LOCK TABLES `AreaRuleVersions` WRITE;
/*!40000 ALTER TABLE `AreaRuleVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRuleVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRules`
--

DROP TABLE IF EXISTS `AreaRules`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRules` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `EformId` int(11) DEFAULT NULL,
  `EformName` longtext DEFAULT NULL,
  `FolderId` int(11) NOT NULL,
  `FolderName` longtext DEFAULT NULL,
  `Alarm` int(11) DEFAULT NULL,
  `Type` int(11) DEFAULT NULL,
  `ChecklistStable` tinyint(1) DEFAULT NULL,
  `TailBite` tinyint(1) DEFAULT NULL,
  `DayOfWeek` int(11) NOT NULL,
  `GroupItemId` int(11) NOT NULL,
  `IsDefault` tinyint(1) NOT NULL,
  `RepeatEvery` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `ComplianceEnabled` tinyint(1) DEFAULT NULL,
  `ComplianceModifiable` tinyint(1) NOT NULL DEFAULT 0,
  `Notifications` tinyint(1) DEFAULT NULL,
  `NotificationsModifiable` tinyint(1) NOT NULL DEFAULT 0,
  `SecondaryeFormId` int(11) NOT NULL DEFAULT 0,
  `SecondaryeFormName` longtext DEFAULT NULL,
  `CreatedInGuide` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaRules_AreaId` (`AreaId`),
  KEY `IX_AreaRules_PropertyId` (`PropertyId`),
  CONSTRAINT `FK_AreaRules_Areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `Areas` (`Id`),
  CONSTRAINT `FK_AreaRules_Properties_PropertyId` FOREIGN KEY (`PropertyId`) REFERENCES `Properties` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRules`
--

LOCK TABLES `AreaRules` WRITE;
/*!40000 ALTER TABLE `AreaRules` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRules` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaRulesPlanningVersions`
--

DROP TABLE IF EXISTS `AreaRulesPlanningVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaRulesPlanningVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRulePlanningId` int(11) NOT NULL,
  `StartDate` datetime(6) DEFAULT NULL,
  `EndDate` datetime(6) DEFAULT NULL,
  `DayOfWeek` int(11) NOT NULL,
  `DayOfMonth` int(11) NOT NULL,
  `RepeatEvery` int(11) DEFAULT NULL,
  `RepeatType` int(11) DEFAULT NULL,
  `Status` tinyint(1) NOT NULL,
  `SendNotifications` tinyint(1) NOT NULL,
  `Alarm` int(11) NOT NULL,
  `Type` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `ItemPlanningId` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `HoursAndEnergyEnabled` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL DEFAULT 0,
  `AreaId` int(11) NOT NULL DEFAULT 0,
  `ComplianceEnabled` tinyint(1) NOT NULL DEFAULT 1,
  `UseStartDateAsStartOfPeriod` tinyint(1) NOT NULL DEFAULT 0,
  `ItemPlanningTagId` int(11) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaRulesPlanningVersions`
--

LOCK TABLES `AreaRulesPlanningVersions` WRITE;
/*!40000 ALTER TABLE `AreaRulesPlanningVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaRulesPlanningVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaTranslationVersions`
--

DROP TABLE IF EXISTS `AreaTranslationVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaTranslationVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `AreaTranslationId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `InfoBox` longtext DEFAULT NULL,
  `Placeholder` longtext DEFAULT NULL,
  `NewItemName` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaTranslationVersions`
--

LOCK TABLES `AreaTranslationVersions` WRITE;
/*!40000 ALTER TABLE `AreaTranslationVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AreaTranslationVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaTranslations`
--

DROP TABLE IF EXISTS `AreaTranslations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaTranslations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `InfoBox` longtext DEFAULT NULL,
  `Placeholder` longtext DEFAULT NULL,
  `NewItemName` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AreaTranslations_AreaId` (`AreaId`),
  CONSTRAINT `FK_AreaTranslations_Areas_AreaId` FOREIGN KEY (`AreaId`) REFERENCES `Areas` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=10 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaTranslations`
--

LOCK TABLES `AreaTranslations` WRITE;
/*!40000 ALTER TABLE `AreaTranslations` DISABLE KEYS */;
INSERT INTO `AreaTranslations` VALUES (1,3,'03. Flydelag','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En gyllebeholder pr. linje','Gyllebeholder','Ny flydelag'),(2,3,'03. Floating layer','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One slurry tank per line','Slurry tank','New floating layer'),(3,3,'03. Schwimmende Ebene','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Gülle-Tank pro Zeile','Gülle-Tank','Neue Schwimmende Ebene'),(4,5,'05. Halebid','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En stald pr. linje','Stald','Ny stald til klargøring'),(5,5,'05. Tail bite','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One stable per line','Stable','New stable'),(6,5,'05. Schwanzbiss','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Ställe pro Zeile','Ställe','Neue Ställe'),(7,31,'00. Logbøger','',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et fokusområde pr. linje','Fokusområde','Nyt fokusområde'),(8,31,'01. Log books','',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'An area of focus per line','Area of focus','New area of focus'),(9,31,'01. Logbücher','',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ein Fokusområde pro Zeile','Fokusbereich','Neues Fokusområde');
/*!40000 ALTER TABLE `AreaTranslations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AreaVersions`
--

DROP TABLE IF EXISTS `AreaVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AreaVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Type` int(11) NOT NULL,
  `ItemPlanningTagId` int(11) NOT NULL,
  `AreaId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `IsFarm` tinyint(1) NOT NULL DEFAULT 1,
  `IsDisabled` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaVersions`
--

LOCK TABLES `AreaVersions` WRITE;
/*!40000 ALTER TABLE `AreaVersions` DISABLE KEYS */;
INSERT INTO `AreaVersions` VALUES (1,2,0,3,'2024-06-13 09:57:37.678371','2024-06-13 09:57:37.678373','created',0,0,1,1,0),(2,3,0,5,'2024-06-13 09:57:38.078308','2024-06-13 09:57:38.078310','created',0,0,1,1,0),(3,1,0,31,'2024-06-13 09:57:38.847612','2024-06-13 09:57:38.847614','created',0,0,1,1,0);
/*!40000 ALTER TABLE `AreaVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Areas`
--

DROP TABLE IF EXISTS `Areas`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Areas` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Type` int(11) NOT NULL,
  `ItemPlanningTagId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `IsFarm` tinyint(1) NOT NULL DEFAULT 1,
  `IsDisabled` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=32 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Areas`
--

LOCK TABLES `Areas` WRITE;
/*!40000 ALTER TABLE `Areas` DISABLE KEYS */;
INSERT INTO `Areas` VALUES (3,2,0,'2024-06-13 09:57:37.678371','2024-06-13 09:57:37.678373','created',0,0,1,1,0),(5,3,0,'2024-06-13 09:57:38.078308','2024-06-13 09:57:38.078310','created',0,0,1,1,0),(31,1,0,'2024-06-13 09:57:38.847612','2024-06-13 09:57:38.847614','created',0,0,1,1,0);
/*!40000 ALTER TABLE `Areas` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ChemicalProductPropertieSites`
--

DROP TABLE IF EXISTS `ChemicalProductPropertieSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ChemicalProductPropertieSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalId` int(11) NOT NULL,
  `ProductId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `SdkSiteId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `LanguageId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ChemicalProductPropertieSites`
--

LOCK TABLES `ChemicalProductPropertieSites` WRITE;
/*!40000 ALTER TABLE `ChemicalProductPropertieSites` DISABLE KEYS */;
/*!40000 ALTER TABLE `ChemicalProductPropertieSites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ChemicalProductProperties`
--

DROP TABLE IF EXISTS `ChemicalProductProperties`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ChemicalProductProperties` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalId` int(11) NOT NULL,
  `ProductId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL DEFAULT 0,
  `Locations` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL DEFAULT 0,
  `SdkSiteId` int(11) NOT NULL DEFAULT 0,
  `ExpireDate` datetime(6) DEFAULT NULL,
  `LastFolderName` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ChemicalProductProperties`
--

LOCK TABLES `ChemicalProductProperties` WRITE;
/*!40000 ALTER TABLE `ChemicalProductProperties` DISABLE KEYS */;
/*!40000 ALTER TABLE `ChemicalProductProperties` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ChemicalProductPropertyVersionSites`
--

DROP TABLE IF EXISTS `ChemicalProductPropertyVersionSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ChemicalProductPropertyVersionSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalId` int(11) NOT NULL,
  `ProductId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `SdkSiteId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `LanguageId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ChemicalProductPropertyVersionSites`
--

LOCK TABLES `ChemicalProductPropertyVersionSites` WRITE;
/*!40000 ALTER TABLE `ChemicalProductPropertyVersionSites` DISABLE KEYS */;
/*!40000 ALTER TABLE `ChemicalProductPropertyVersionSites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ChemicalProductPropertyVersions`
--

DROP TABLE IF EXISTS `ChemicalProductPropertyVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ChemicalProductPropertyVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalProductPropertyId` int(11) NOT NULL,
  `ChemicalId` int(11) NOT NULL,
  `ProductId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL DEFAULT 0,
  `Locations` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL DEFAULT 0,
  `SdkSiteId` int(11) NOT NULL DEFAULT 0,
  `ExpireDate` datetime(6) DEFAULT NULL,
  `LastFolderName` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ChemicalProductPropertyVersions`
--

LOCK TABLES `ChemicalProductPropertyVersions` WRITE;
/*!40000 ALTER TABLE `ChemicalProductPropertyVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ChemicalProductPropertyVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ComplianceVersions`
--

DROP TABLE IF EXISTS `ComplianceVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ComplianceVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ItemName` longtext DEFAULT NULL,
  `AreaId` int(11) NOT NULL DEFAULT 0,
  `AreaName` longtext DEFAULT NULL,
  `PlanningId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `Deadline` datetime(6) NOT NULL,
  `StartDate` datetime(6) NOT NULL,
  `ComplianceId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `MicrotingSdkCaseId` int(11) NOT NULL DEFAULT 0,
  `MicrotingSdkeFormId` int(11) NOT NULL DEFAULT 0,
  `CheckListSiteId` int(11) NOT NULL DEFAULT 0,
  `PlanningCaseSiteId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ComplianceVersions`
--

LOCK TABLES `ComplianceVersions` WRITE;
/*!40000 ALTER TABLE `ComplianceVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ComplianceVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Compliances`
--

DROP TABLE IF EXISTS `Compliances`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Compliances` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ItemName` longtext DEFAULT NULL,
  `AreaId` int(11) NOT NULL DEFAULT 0,
  `AreaName` longtext DEFAULT NULL,
  `PlanningId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `Deadline` datetime(6) NOT NULL,
  `StartDate` datetime(6) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `MicrotingSdkCaseId` int(11) NOT NULL DEFAULT 0,
  `MicrotingSdkeFormId` int(11) NOT NULL DEFAULT 0,
  `CheckListSiteId` int(11) NOT NULL DEFAULT 0,
  `PlanningCaseSiteId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_PlanningId_Deadline` (`PlanningId`,`Deadline`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Compliances`
--

LOCK TABLES `Compliances` WRITE;
/*!40000 ALTER TABLE `Compliances` DISABLE KEYS */;
/*!40000 ALTER TABLE `Compliances` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EmailAttachmentVersions`
--

DROP TABLE IF EXISTS `EmailAttachmentVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EmailAttachmentVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `EmailAttachmentId` int(11) NOT NULL,
  `EmailId` int(11) NOT NULL,
  `ResourceName` longtext DEFAULT NULL,
  `CidName` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EmailAttachmentVersions`
--

LOCK TABLES `EmailAttachmentVersions` WRITE;
/*!40000 ALTER TABLE `EmailAttachmentVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `EmailAttachmentVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EmailAttachments`
--

DROP TABLE IF EXISTS `EmailAttachments`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EmailAttachments` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `EmailId` int(11) NOT NULL,
  `ResourceName` longtext DEFAULT NULL,
  `CidName` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_EmailAttachments_EmailId` (`EmailId`),
  CONSTRAINT `FK_EmailAttachments_Emails_EmailId` FOREIGN KEY (`EmailId`) REFERENCES `Emails` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EmailAttachments`
--

LOCK TABLES `EmailAttachments` WRITE;
/*!40000 ALTER TABLE `EmailAttachments` DISABLE KEYS */;
/*!40000 ALTER TABLE `EmailAttachments` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EmailVersions`
--

DROP TABLE IF EXISTS `EmailVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EmailVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `To` longtext DEFAULT NULL,
  `From` longtext DEFAULT NULL,
  `Subject` longtext DEFAULT NULL,
  `Body` longtext DEFAULT NULL,
  `BodyType` longtext DEFAULT NULL,
  `Status` longtext DEFAULT NULL,
  `Error` longtext DEFAULT NULL,
  `Sent` longtext DEFAULT NULL,
  `SentAt` datetime(6) NOT NULL,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `DelayedUntil` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EmailVersions`
--

LOCK TABLES `EmailVersions` WRITE;
/*!40000 ALTER TABLE `EmailVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `EmailVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Emails`
--

DROP TABLE IF EXISTS `Emails`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Emails` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `To` longtext DEFAULT NULL,
  `From` longtext DEFAULT NULL,
  `Subject` longtext DEFAULT NULL,
  `Body` longtext DEFAULT NULL,
  `BodyType` longtext DEFAULT NULL,
  `Status` longtext DEFAULT NULL,
  `Error` longtext DEFAULT NULL,
  `Sent` longtext DEFAULT NULL,
  `SentAt` datetime(6) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `DelayedUntil` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Emails`
--

LOCK TABLES `Emails` WRITE;
/*!40000 ALTER TABLE `Emails` DISABLE KEYS */;
/*!40000 ALTER TABLE `Emails` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FileTagVersions`
--

DROP TABLE IF EXISTS `FileTagVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FileTagVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` longtext DEFAULT NULL,
  `FileTagId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FileTagVersions`
--

LOCK TABLES `FileTagVersions` WRITE;
/*!40000 ALTER TABLE `FileTagVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `FileTagVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FileTags`
--

DROP TABLE IF EXISTS `FileTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FileTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` varchar(250) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FileTags`
--

LOCK TABLES `FileTags` WRITE;
/*!40000 ALTER TABLE `FileTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `FileTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FileVersions`
--

DROP TABLE IF EXISTS `FileVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FileVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FileName` longtext DEFAULT NULL,
  `FileId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FileVersions`
--

LOCK TABLES `FileVersions` WRITE;
/*!40000 ALTER TABLE `FileVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `FileVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Files`
--

DROP TABLE IF EXISTS `Files`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Files` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FileName` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Files`
--

LOCK TABLES `Files` WRITE;
/*!40000 ALTER TABLE `Files` DISABLE KEYS */;
/*!40000 ALTER TABLE `Files` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FilesTags`
--

DROP TABLE IF EXISTS `FilesTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FilesTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FileTagId` int(11) NOT NULL,
  `FileId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_FilesTags_FileId` (`FileId`),
  KEY `IX_FilesTags_FileTagId` (`FileTagId`),
  CONSTRAINT `FK_FilesTags_FileTags_FileTagId` FOREIGN KEY (`FileTagId`) REFERENCES `FileTags` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_FilesTags_Files_FileId` FOREIGN KEY (`FileId`) REFERENCES `Files` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FilesTags`
--

LOCK TABLES `FilesTags` WRITE;
/*!40000 ALTER TABLE `FilesTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `FilesTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FilesTagsVersions`
--

DROP TABLE IF EXISTS `FilesTagsVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FilesTagsVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FileTagId` int(11) NOT NULL,
  `FileId` int(11) NOT NULL,
  `FileTagsId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FilesTagsVersions`
--

LOCK TABLES `FilesTagsVersions` WRITE;
/*!40000 ALTER TABLE `FilesTagsVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `FilesTagsVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningSites`
--

DROP TABLE IF EXISTS `PlanningSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRulePlanningsId` int(11) NOT NULL,
  `SiteId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `AreaId` int(11) DEFAULT NULL,
  `AreaRuleId` int(11) DEFAULT NULL,
  `Status` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanningSites_AreaRulePlanningsId` (`AreaRulePlanningsId`),
  CONSTRAINT `FK_PlanningSites_AreaRulePlannings_AreaRulePlanningsId` FOREIGN KEY (`AreaRulePlanningsId`) REFERENCES `AreaRulePlannings` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningSites`
--

LOCK TABLES `PlanningSites` WRITE;
/*!40000 ALTER TABLE `PlanningSites` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningSites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningSitesVersions`
--

DROP TABLE IF EXISTS `PlanningSitesVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningSitesVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PlanningSiteId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `SiteId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `AreaId` int(11) DEFAULT NULL,
  `AreaRuleId` int(11) DEFAULT NULL,
  `Status` int(11) NOT NULL DEFAULT 0,
  `AreaRulePlanningsId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningSitesVersions`
--

LOCK TABLES `PlanningSitesVersions` WRITE;
/*!40000 ALTER TABLE `PlanningSitesVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningSitesVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PluginConfigurationValueVersions`
--

DROP TABLE IF EXISTS `PluginConfigurationValueVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PluginConfigurationValueVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` longtext DEFAULT NULL,
  `Value` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginConfigurationValueVersions`
--

LOCK TABLES `PluginConfigurationValueVersions` WRITE;
/*!40000 ALTER TABLE `PluginConfigurationValueVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PluginConfigurationValueVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PluginConfigurationValues`
--

DROP TABLE IF EXISTS `PluginConfigurationValues`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PluginConfigurationValues` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` longtext DEFAULT NULL,
  `Value` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginConfigurationValues`
--

LOCK TABLES `PluginConfigurationValues` WRITE;
/*!40000 ALTER TABLE `PluginConfigurationValues` DISABLE KEYS */;
INSERT INTO `PluginConfigurationValues` VALUES (1,'BackendConfigurationSettings:ReportSubHeaderName','','2024-06-13 09:56:33.342157','2024-06-13 09:56:33.342159','created',1,0,1),(2,'BackendConfigurationSettings:ReportHeaderName','','2024-06-13 09:56:33.368565','2024-06-13 09:56:33.368567','created',1,0,1),(3,'BackendConfigurationSettings:MaxChrNumbers','1000','2024-06-13 09:56:33.372442','2024-06-13 09:56:33.372443','created',1,0,1),(4,'BackendConfigurationSettings:MaxCvrNumbers','1000','2024-06-13 09:56:33.375592','2024-06-13 09:56:33.375594','created',1,0,1);
/*!40000 ALTER TABLE `PluginConfigurationValues` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PluginGroupPermissionVersions`
--

DROP TABLE IF EXISTS `PluginGroupPermissionVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PluginGroupPermissionVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `GroupId` int(11) NOT NULL,
  `PermissionId` int(11) NOT NULL,
  `IsEnabled` tinyint(1) NOT NULL,
  `PluginGroupPermissionId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissionVersions`
--

LOCK TABLES `PluginGroupPermissionVersions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissionVersions` DISABLE KEYS */;
INSERT INTO `PluginGroupPermissionVersions` VALUES (1,1,1,1,1,'2024-06-13 09:56:33.994609','2024-06-13 09:56:33.994611','created',0,0,1),(2,1,5,1,2,'2024-06-13 09:56:34.491161','2024-06-13 09:56:34.491163','created',0,0,1),(3,1,6,1,3,'2024-06-13 09:56:34.612039','2024-06-13 09:56:34.612041','created',0,0,1),(4,1,2,1,4,'2024-06-13 09:56:34.751694','2024-06-13 09:56:34.751696','created',0,0,1),(5,1,3,1,5,'2024-06-13 09:56:34.812371','2024-06-13 09:56:34.812373','created',0,0,1),(6,1,4,1,6,'2024-06-13 09:56:34.993958','2024-06-13 09:56:34.993961','created',0,0,1),(7,1,7,1,7,'2024-06-13 09:56:35.140614','2024-06-13 09:56:35.140615','created',0,0,1),(8,1,8,1,8,'2024-06-13 09:56:35.240748','2024-06-13 09:56:35.240750','created',0,0,1);
/*!40000 ALTER TABLE `PluginGroupPermissionVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PluginGroupPermissions`
--

DROP TABLE IF EXISTS `PluginGroupPermissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PluginGroupPermissions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `GroupId` int(11) NOT NULL,
  `PermissionId` int(11) NOT NULL,
  `IsEnabled` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PluginGroupPermissions_PermissionId` (`PermissionId`),
  CONSTRAINT `FK_PluginGroupPermissions_PluginPermissions_PermissionId` FOREIGN KEY (`PermissionId`) REFERENCES `PluginPermissions` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissions`
--

LOCK TABLES `PluginGroupPermissions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissions` DISABLE KEYS */;
INSERT INTO `PluginGroupPermissions` VALUES (1,1,1,1,'2024-06-13 09:56:33.994609','2024-06-13 09:56:33.994611','created',0,0,1),(2,1,5,1,'2024-06-13 09:56:34.491161','2024-06-13 09:56:34.491163','created',0,0,1),(3,1,6,1,'2024-06-13 09:56:34.612039','2024-06-13 09:56:34.612041','created',0,0,1),(4,1,2,1,'2024-06-13 09:56:34.751694','2024-06-13 09:56:34.751696','created',0,0,1),(5,1,3,1,'2024-06-13 09:56:34.812371','2024-06-13 09:56:34.812373','created',0,0,1),(6,1,4,1,'2024-06-13 09:56:34.993958','2024-06-13 09:56:34.993961','created',0,0,1),(7,1,7,1,'2024-06-13 09:56:35.140614','2024-06-13 09:56:35.140615','created',0,0,1),(8,1,8,1,'2024-06-13 09:56:35.240748','2024-06-13 09:56:35.240750','created',0,0,1);
/*!40000 ALTER TABLE `PluginGroupPermissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PluginPermissions`
--

DROP TABLE IF EXISTS `PluginPermissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PluginPermissions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PermissionName` longtext DEFAULT NULL,
  `ClaimName` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginPermissions`
--

LOCK TABLES `PluginPermissions` WRITE;
/*!40000 ALTER TABLE `PluginPermissions` DISABLE KEYS */;
INSERT INTO `PluginPermissions` VALUES (1,'Access BackendConfiguration Plugin','backend_configuration_plugin_access','2024-06-13 09:56:33.382817',NULL,'created',1,0,1),(2,'Create property','properties_create','2024-06-13 09:56:33.398168',NULL,'created',1,0,1),(3,'Get properties','properties_get','2024-06-13 09:56:33.400505',NULL,'created',1,0,1),(4,'Edit property','property_edit','2024-06-13 09:56:33.403103',NULL,'created',1,0,1),(5,'Enable chemical management','chemical_management_enable','2024-06-13 09:56:33.404446',NULL,'created',1,0,1),(6,'Enable document management','document_management_enable','2024-06-13 09:56:33.405668',NULL,'created',1,0,1),(7,'Enable task management','task_management_enable','2024-06-13 09:56:33.406831',NULL,'created',1,0,1),(8,'Enable time registration','time_registration_enable','2024-06-13 09:56:33.407895',NULL,'created',1,0,1);
/*!40000 ALTER TABLE `PluginPermissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolAccidentVersions`
--

DROP TABLE IF EXISTS `PoolAccidentVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolAccidentVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PoolAccidentId` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `Time` time(6) NOT NULL,
  `SolidFaeces` tinyint(1) NOT NULL,
  `DiarrheaLoose` tinyint(1) NOT NULL,
  `Vomit` tinyint(1) NOT NULL,
  `ContactedPersonId` int(11) NOT NULL,
  `OwnPersonId` int(11) NOT NULL,
  `Comment` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL DEFAULT 0,
  `PlanningId` int(11) NOT NULL DEFAULT 0,
  `SdkCaseId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolAccidentVersions`
--

LOCK TABLES `PoolAccidentVersions` WRITE;
/*!40000 ALTER TABLE `PoolAccidentVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolAccidentVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolAccidents`
--

DROP TABLE IF EXISTS `PoolAccidents`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolAccidents` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `Time` time(6) NOT NULL,
  `SolidFaeces` tinyint(1) NOT NULL,
  `DiarrheaLoose` tinyint(1) NOT NULL,
  `Vomit` tinyint(1) NOT NULL,
  `ContactedPersonId` int(11) NOT NULL,
  `OwnPersonId` int(11) NOT NULL,
  `Comment` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL DEFAULT 0,
  `PlanningId` int(11) NOT NULL DEFAULT 0,
  `SdkCaseId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolAccidents`
--

LOCK TABLES `PoolAccidents` WRITE;
/*!40000 ALTER TABLE `PoolAccidents` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolAccidents` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolHistorySiteVersions`
--

DROP TABLE IF EXISTS `PoolHistorySiteVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolHistorySiteVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `SiteId` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `PoolHistorySiteId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolHistorySiteVersions`
--

LOCK TABLES `PoolHistorySiteVersions` WRITE;
/*!40000 ALTER TABLE `PoolHistorySiteVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolHistorySiteVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolHistorySites`
--

DROP TABLE IF EXISTS `PoolHistorySites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolHistorySites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `SiteId` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolHistorySites`
--

LOCK TABLES `PoolHistorySites` WRITE;
/*!40000 ALTER TABLE `PoolHistorySites` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolHistorySites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolHourResultVersions`
--

DROP TABLE IF EXISTS `PoolHourResultVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolHourResultVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PoolHourResultId` int(11) NOT NULL,
  `PoolHourId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `PulseRateAtOpening` double DEFAULT NULL,
  `ReadPhValue` double DEFAULT NULL,
  `ReadFreeChlorine` double DEFAULT NULL,
  `ReadTemperature` double DEFAULT NULL,
  `NumberOfGuestsAtClosing` double DEFAULT NULL,
  `Clarity` longtext DEFAULT NULL,
  `MeasuredFreeChlorine` double DEFAULT NULL,
  `MeasuredTotalChlorine` double DEFAULT NULL,
  `MeasuredBoundChlorine` double DEFAULT NULL,
  `MeasuredPh` double DEFAULT NULL,
  `AcknowledgmentOfPulseRateAtOpening` longtext DEFAULT NULL,
  `MeasuredTempDuringTheDay` double DEFAULT NULL,
  `Comment` longtext DEFAULT NULL,
  `DoneByUserId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL DEFAULT 0,
  `DoneByUserName` longtext DEFAULT NULL,
  `DoneAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolHourResultVersions`
--

LOCK TABLES `PoolHourResultVersions` WRITE;
/*!40000 ALTER TABLE `PoolHourResultVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolHourResultVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolHourResults`
--

DROP TABLE IF EXISTS `PoolHourResults`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolHourResults` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PoolHourId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `AreaRuleId` int(11) NOT NULL,
  `PulseRateAtOpening` double DEFAULT NULL,
  `ReadPhValue` double DEFAULT NULL,
  `ReadFreeChlorine` double DEFAULT NULL,
  `ReadTemperature` double DEFAULT NULL,
  `NumberOfGuestsAtClosing` double DEFAULT NULL,
  `Clarity` longtext DEFAULT NULL,
  `MeasuredFreeChlorine` double DEFAULT NULL,
  `MeasuredTotalChlorine` double DEFAULT NULL,
  `MeasuredBoundChlorine` double DEFAULT NULL,
  `MeasuredPh` double DEFAULT NULL,
  `AcknowledgmentOfPulseRateAtOpening` longtext DEFAULT NULL,
  `MeasuredTempDuringTheDay` double DEFAULT NULL,
  `Comment` longtext DEFAULT NULL,
  `DoneByUserId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL DEFAULT 0,
  `DoneByUserName` longtext DEFAULT NULL,
  `DoneAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolHourResults`
--

LOCK TABLES `PoolHourResults` WRITE;
/*!40000 ALTER TABLE `PoolHourResults` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolHourResults` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolHourVersions`
--

DROP TABLE IF EXISTS `PoolHourVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolHourVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleId` int(11) NOT NULL,
  `DayOfWeek` int(11) NOT NULL,
  `Index` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `ItemsPlanningId` int(11) DEFAULT NULL,
  `PoolHourId` int(11) NOT NULL,
  `IsActive` tinyint(1) NOT NULL,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolHourVersions`
--

LOCK TABLES `PoolHourVersions` WRITE;
/*!40000 ALTER TABLE `PoolHourVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolHourVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PoolHours`
--

DROP TABLE IF EXISTS `PoolHours`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PoolHours` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `AreaRuleId` int(11) NOT NULL,
  `DayOfWeek` int(11) NOT NULL,
  `Index` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `ItemsPlanningId` int(11) DEFAULT NULL,
  `IsActive` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PoolHours`
--

LOCK TABLES `PoolHours` WRITE;
/*!40000 ALTER TABLE `PoolHours` DISABLE KEYS */;
/*!40000 ALTER TABLE `PoolHours` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertieVersions`
--

DROP TABLE IF EXISTS `PropertieVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertieVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `CHR` longtext DEFAULT NULL,
  `Address` longtext DEFAULT NULL,
  `FolderId` int(11) DEFAULT NULL,
  `ItemPlanningTagId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `CVR` longtext DEFAULT NULL,
  `ComplianceStatus` int(11) NOT NULL DEFAULT 0,
  `ComplianceStatusThirty` int(11) NOT NULL DEFAULT 0,
  `FolderIdForTasks` int(11) DEFAULT NULL,
  `WorkorderEnable` tinyint(1) NOT NULL DEFAULT 0,
  `EntitySelectListAreas` int(11) DEFAULT NULL,
  `EntitySelectListDeviceUsers` int(11) DEFAULT NULL,
  `FolderIdForCompletedTasks` int(11) DEFAULT NULL,
  `FolderIdForNewTasks` int(11) DEFAULT NULL,
  `FolderIdForOngoingTasks` int(11) DEFAULT NULL,
  `EntitySearchListChemicals` int(11) DEFAULT NULL,
  `ChemicalLastUpdatedAt` datetime(6) DEFAULT NULL,
  `EntitySearchListChemicalRegNos` int(11) DEFAULT NULL,
  `IndustryCode` longtext DEFAULT NULL,
  `IsFarm` tinyint(1) NOT NULL DEFAULT 0,
  `EntitySearchListPoolWorkers` int(11) DEFAULT NULL,
  `EntitySelectListChemicalAreas` int(11) DEFAULT NULL,
  `MainMailAddress` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertieVersions`
--

LOCK TABLES `PropertieVersions` WRITE;
/*!40000 ALTER TABLE `PropertieVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertieVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Properties`
--

DROP TABLE IF EXISTS `Properties`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Properties` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` longtext DEFAULT NULL,
  `CHR` longtext DEFAULT NULL,
  `Address` longtext DEFAULT NULL,
  `FolderId` int(11) DEFAULT NULL,
  `ItemPlanningTagId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `CVR` longtext DEFAULT NULL,
  `ComplianceStatus` int(11) NOT NULL DEFAULT 0,
  `ComplianceStatusThirty` int(11) NOT NULL DEFAULT 0,
  `FolderIdForTasks` int(11) DEFAULT NULL,
  `WorkorderEnable` tinyint(1) NOT NULL DEFAULT 0,
  `EntitySelectListAreas` int(11) DEFAULT NULL,
  `EntitySelectListDeviceUsers` int(11) DEFAULT NULL,
  `FolderIdForCompletedTasks` int(11) DEFAULT NULL,
  `FolderIdForNewTasks` int(11) DEFAULT NULL,
  `FolderIdForOngoingTasks` int(11) DEFAULT NULL,
  `EntitySearchListChemicals` int(11) DEFAULT NULL,
  `ChemicalLastUpdatedAt` datetime(6) DEFAULT NULL,
  `EntitySearchListChemicalRegNos` int(11) DEFAULT NULL,
  `IndustryCode` longtext DEFAULT NULL,
  `IsFarm` tinyint(1) NOT NULL DEFAULT 0,
  `EntitySearchListPoolWorkers` int(11) DEFAULT NULL,
  `EntitySelectListChemicalAreas` int(11) DEFAULT NULL,
  `MainMailAddress` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Properties`
--

LOCK TABLES `Properties` WRITE;
/*!40000 ALTER TABLE `Properties` DISABLE KEYS */;
/*!40000 ALTER TABLE `Properties` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertyFileVersions`
--

DROP TABLE IF EXISTS `PropertyFileVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertyFileVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyFileId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `FileId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertyFileVersions`
--

LOCK TABLES `PropertyFileVersions` WRITE;
/*!40000 ALTER TABLE `PropertyFileVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertyFileVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertyFiles`
--

DROP TABLE IF EXISTS `PropertyFiles`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertyFiles` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyId` int(11) NOT NULL,
  `FileId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PropertyFiles_FileId` (`FileId`),
  KEY `IX_PropertyFiles_PropertyId` (`PropertyId`),
  CONSTRAINT `FK_PropertyFiles_Files_FileId` FOREIGN KEY (`FileId`) REFERENCES `Files` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_PropertyFiles_Properties_PropertyId` FOREIGN KEY (`PropertyId`) REFERENCES `Properties` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertyFiles`
--

LOCK TABLES `PropertyFiles` WRITE;
/*!40000 ALTER TABLE `PropertyFiles` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertyFiles` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertySelectedLanguageVersions`
--

DROP TABLE IF EXISTS `PropertySelectedLanguageVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertySelectedLanguageVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertySelectedLanguageId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `LanguageId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertySelectedLanguageVersions`
--

LOCK TABLES `PropertySelectedLanguageVersions` WRITE;
/*!40000 ALTER TABLE `PropertySelectedLanguageVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertySelectedLanguageVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertySelectedLanguages`
--

DROP TABLE IF EXISTS `PropertySelectedLanguages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertySelectedLanguages` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyId` int(11) NOT NULL,
  `LanguageId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PropertySelectedLanguages_PropertyId` (`PropertyId`),
  CONSTRAINT `FK_PropertySelectedLanguages_Properties_PropertyId` FOREIGN KEY (`PropertyId`) REFERENCES `Properties` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertySelectedLanguages`
--

LOCK TABLES `PropertySelectedLanguages` WRITE;
/*!40000 ALTER TABLE `PropertySelectedLanguages` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertySelectedLanguages` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertyWorkerVersions`
--

DROP TABLE IF EXISTS `PropertyWorkerVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertyWorkerVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyId` int(11) NOT NULL,
  `WorkerId` int(11) NOT NULL,
  `PropertyWorkerId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EntityItemId` int(11) DEFAULT NULL,
  `TaskManagementEnabled` tinyint(1) DEFAULT NULL,
  `PinCode` varchar(50) DEFAULT NULL,
  `EmployeeNo` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertyWorkerVersions`
--

LOCK TABLES `PropertyWorkerVersions` WRITE;
/*!40000 ALTER TABLE `PropertyWorkerVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertyWorkerVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PropertyWorkers`
--

DROP TABLE IF EXISTS `PropertyWorkers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PropertyWorkers` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyId` int(11) NOT NULL,
  `WorkerId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EntityItemId` int(11) DEFAULT NULL,
  `TaskManagementEnabled` tinyint(1) DEFAULT NULL,
  `PinCode` varchar(50) DEFAULT NULL,
  `EmployeeNo` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PropertyWorkers_PropertyId` (`PropertyId`),
  CONSTRAINT `FK_PropertyWorkers_Properties_PropertyId` FOREIGN KEY (`PropertyId`) REFERENCES `Properties` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PropertyWorkers`
--

LOCK TABLES `PropertyWorkers` WRITE;
/*!40000 ALTER TABLE `PropertyWorkers` DISABLE KEYS */;
/*!40000 ALTER TABLE `PropertyWorkers` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ProperyAreaFolderVersions`
--

DROP TABLE IF EXISTS `ProperyAreaFolderVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ProperyAreaFolderVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ProperyAreaFolderId` int(11) NOT NULL,
  `ProperyAreaAsignmentId` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ProperyAreaFolderVersions`
--

LOCK TABLES `ProperyAreaFolderVersions` WRITE;
/*!40000 ALTER TABLE `ProperyAreaFolderVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ProperyAreaFolderVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ProperyAreaFolders`
--

DROP TABLE IF EXISTS `ProperyAreaFolders`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ProperyAreaFolders` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ProperyAreaAsignmentId` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_ProperyAreaFolders_ProperyAreaAsignmentId` (`ProperyAreaAsignmentId`),
  CONSTRAINT `FK_ProperyAreaFolders_AreaProperties_ProperyAreaAsignmentId` FOREIGN KEY (`ProperyAreaAsignmentId`) REFERENCES `AreaProperties` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ProperyAreaFolders`
--

LOCK TABLES `ProperyAreaFolders` WRITE;
/*!40000 ALTER TABLE `ProperyAreaFolders` DISABLE KEYS */;
/*!40000 ALTER TABLE `ProperyAreaFolders` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `TaskTrackerColumnVersions`
--

DROP TABLE IF EXISTS `TaskTrackerColumnVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `TaskTrackerColumnVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `TaskTrackerColumnId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  `ColumnName` longtext DEFAULT NULL,
  `IsColumnEnabled` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `TaskTrackerColumnVersions`
--

LOCK TABLES `TaskTrackerColumnVersions` WRITE;
/*!40000 ALTER TABLE `TaskTrackerColumnVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `TaskTrackerColumnVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `TaskTrackerColumns`
--

DROP TABLE IF EXISTS `TaskTrackerColumns`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `TaskTrackerColumns` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `ColumnName` longtext DEFAULT NULL,
  `isColumnEnabled` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `TaskTrackerColumns`
--

LOCK TABLES `TaskTrackerColumns` WRITE;
/*!40000 ALTER TABLE `TaskTrackerColumns` DISABLE KEYS */;
/*!40000 ALTER TABLE `TaskTrackerColumns` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UploadedDataVersions`
--

DROP TABLE IF EXISTS `UploadedDataVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UploadedDataVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FileId` int(11) NOT NULL,
  `Checksum` varchar(255) DEFAULT NULL,
  `Extension` varchar(255) DEFAULT NULL,
  `UploaderType` varchar(255) DEFAULT NULL,
  `FileLocation` varchar(255) DEFAULT NULL,
  `FileName` varchar(255) DEFAULT NULL,
  `UploadedDataId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `UploadedDataVersions`
--

LOCK TABLES `UploadedDataVersions` WRITE;
/*!40000 ALTER TABLE `UploadedDataVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `UploadedDataVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UploadedDatas`
--

DROP TABLE IF EXISTS `UploadedDatas`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UploadedDatas` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FileId` int(11) NOT NULL,
  `Checksum` varchar(255) DEFAULT NULL,
  `Extension` varchar(255) DEFAULT NULL,
  `UploaderType` varchar(255) DEFAULT NULL,
  `FileLocation` varchar(255) DEFAULT NULL,
  `FileName` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_UploadedDatas_FileId` (`FileId`),
  CONSTRAINT `FK_UploadedDatas_Files_FileId` FOREIGN KEY (`FileId`) REFERENCES `Files` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `UploadedDatas`
--

LOCK TABLES `UploadedDatas` WRITE;
/*!40000 ALTER TABLE `UploadedDatas` DISABLE KEYS */;
/*!40000 ALTER TABLE `UploadedDatas` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `WorkorderCaseImageVersions`
--

DROP TABLE IF EXISTS `WorkorderCaseImageVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `WorkorderCaseImageVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `WorkorderCaseImageId` int(11) NOT NULL,
  `WorkorderCaseId` int(11) NOT NULL,
  `UploadedDataId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `WorkorderCaseImageVersions`
--

LOCK TABLES `WorkorderCaseImageVersions` WRITE;
/*!40000 ALTER TABLE `WorkorderCaseImageVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `WorkorderCaseImageVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `WorkorderCaseImages`
--

DROP TABLE IF EXISTS `WorkorderCaseImages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `WorkorderCaseImages` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `WorkorderCaseId` int(11) NOT NULL,
  `UploadedDataId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `WorkorderCaseImages`
--

LOCK TABLES `WorkorderCaseImages` WRITE;
/*!40000 ALTER TABLE `WorkorderCaseImages` DISABLE KEYS */;
/*!40000 ALTER TABLE `WorkorderCaseImages` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `WorkorderCaseVersions`
--

DROP TABLE IF EXISTS `WorkorderCaseVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `WorkorderCaseVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `WorkorderCaseId` int(11) NOT NULL,
  `PropertyWorkerId` int(11) NOT NULL,
  `CaseId` int(11) NOT NULL,
  `CaseStatusesEnum` int(11) NOT NULL,
  `ParentWorkorderCaseId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EntityItemIdForArea` int(11) DEFAULT NULL,
  `SelectedAreaName` longtext DEFAULT NULL,
  `CreatedByName` longtext DEFAULT NULL,
  `CreatedByText` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `CaseInitiated` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `LastAssignedToName` longtext DEFAULT NULL,
  `LastUpdatedByName` longtext DEFAULT NULL,
  `LeadingCase` tinyint(1) NOT NULL DEFAULT 0,
  `Priority` longtext DEFAULT NULL,
  `AssignedToSdkSiteId` int(11) DEFAULT NULL,
  `CreatedBySdkSiteId` int(11) DEFAULT NULL,
  `UpdatedBySdkSiteId` int(11) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `WorkorderCaseVersions`
--

LOCK TABLES `WorkorderCaseVersions` WRITE;
/*!40000 ALTER TABLE `WorkorderCaseVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `WorkorderCaseVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `WorkorderCases`
--

DROP TABLE IF EXISTS `WorkorderCases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `WorkorderCases` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PropertyWorkerId` int(11) NOT NULL,
  `CaseId` int(11) NOT NULL,
  `CaseStatusesEnum` int(11) NOT NULL,
  `ParentWorkorderCaseId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EntityItemIdForArea` int(11) DEFAULT NULL,
  `SelectedAreaName` longtext DEFAULT NULL,
  `CreatedByName` longtext DEFAULT NULL,
  `CreatedByText` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `CaseInitiated` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `LastAssignedToName` longtext DEFAULT NULL,
  `LastUpdatedByName` longtext DEFAULT NULL,
  `LeadingCase` tinyint(1) NOT NULL DEFAULT 0,
  `Priority` longtext DEFAULT NULL,
  `AssignedToSdkSiteId` int(11) DEFAULT NULL,
  `CreatedBySdkSiteId` int(11) DEFAULT NULL,
  `UpdatedBySdkSiteId` int(11) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_WorkorderCases_ParentWorkorderCaseId` (`ParentWorkorderCaseId`),
  KEY `IX_WorkorderCases_PropertyWorkerId` (`PropertyWorkerId`),
  CONSTRAINT `FK_WorkorderCases_PropertyWorkers_PropertyWorkerId` FOREIGN KEY (`PropertyWorkerId`) REFERENCES `PropertyWorkers` (`Id`),
  CONSTRAINT `FK_WorkorderCases_WorkorderCases_ParentWorkorderCaseId` FOREIGN KEY (`ParentWorkorderCaseId`) REFERENCES `WorkorderCases` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `WorkorderCases`
--

LOCK TABLES `WorkorderCases` WRITE;
/*!40000 ALTER TABLE `WorkorderCases` DISABLE KEYS */;
/*!40000 ALTER TABLE `WorkorderCases` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `__EFMigrationsHistory`
--

DROP TABLE IF EXISTS `__EFMigrationsHistory`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `__EFMigrationsHistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL,
  PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `__EFMigrationsHistory`
--

LOCK TABLES `__EFMigrationsHistory` WRITE;
/*!40000 ALTER TABLE `__EFMigrationsHistory` DISABLE KEYS */;
INSERT INTO `__EFMigrationsHistory` VALUES ('20211025142151_InitialCreate','8.0.6'),('20211110114709_AddingCVRToProperties','8.0.6'),('20220101103446_AddingPropertyIdToAreaRulePlanning','8.0.6'),('20220101111025_AddingAreaIdToAreaRulePlanning','8.0.6'),('20220103152353_AddingComplianceStatusToProperty','8.0.6'),('20220105092557_CreateComplianceClass','8.0.6'),('20220105135743_ChangingAreaIdToInt','8.0.6'),('20220105140315_AddingMoreAttributesToCompliance','8.0.6'),('20220106165734_AddingComplianceStatus30ToProperties','8.0.6'),('20220127172400_AddComplianceEnabledProp','8.0.6'),('20220208172326_AddingPlanningCaseSiteIdAndCheckListSiteId','8.0.6'),('20220210171743_AddWorkorderFlow','8.0.6'),('20220216140313_AddEntitySelectListsForProperty','8.0.6'),('20220217112234_AddingAdditionalFolderIds','8.0.6'),('20220217133829_AddingEntityItemIdToPropertyWorker','8.0.6'),('20220217165554_AddingAttributesToWorkorderCase','8.0.6'),('20220217174831_AddingCreatedByNameToWorkorderCase','8.0.6'),('20220217182010_AddingCreatedByTextToWorkorderCase','8.0.6'),('20220217183352_AddingDescriptionToWorkorderCase','8.0.6'),('20220217184356_AddingCaseInitiatedToWorkorderCase','8.0.6'),('20220217193352_CreateWorkorderCaseImage','8.0.6'),('20220321151057_AddFieldsPlaceholderAndInfoForAreas','8.0.6'),('20220405183346_AddingNewItemName','8.0.6'),('20220417055742_AddingAssignmentAttributesToWorkorderCases','8.0.6'),('20220425054126_AddRepeatTypeToAreaRule','8.0.6'),('20220425151849_AddingMoreAttributesToAreaRule','8.0.6'),('20220426190544_AddingLeadingCaseToWorkorderCase','8.0.6'),('20220603160201_AddingEntitySearchListChemicalsToProperty','8.0.6'),('20220606072856_AddingEntitySearchListChemicalRegNosToProperty','8.0.6'),('20220607114609_CreatingChemicalProductProperty','8.0.6'),('20220609162637_AddingIndustryCodeIsFarmToProperty','8.0.6'),('20220610080134_FixingIndustryCodeTobeString','8.0.6'),('20220612043907_AddingIsFarmToArea','8.0.6'),('20220612164010_AddingPoolHours','8.0.6'),('20220613055856_AddingMorePoolHourTables','8.0.6'),('20220613132009_AllowingItemPlanningIdToBeNull','8.0.6'),('20220614143506_AddingMoreIdsToPlanningSites','8.0.6'),('20220616160212_AddingEntitySearchListPoolWorkers','8.0.6'),('20220621070937_AddingFolderIdToPoolRegistrations','8.0.6'),('20220623095747_ChangingAttributeTypes','8.0.6'),('20220623173220_CreatingPoolHistorySite','8.0.6'),('20220623184950_AllowingValuesToBeNull','8.0.6'),('20220630080523_AddingDoneAtToPoolHourResult','8.0.6'),('20220630161000_AddingSdkCaseIdToChemicalProductProperty','8.0.6'),('20220703144301_AddingChemicalProductPropertySite','8.0.6'),('20220705160545_AddingEntitySelectListChemicalAreasToProperty','8.0.6'),('20220724155019_AddingLocationsToChemicalProductProperty','8.0.6'),('20220806155246_AddingLanguageIdAndSdkSiteIdToChemicalProductProperty','8.0.6'),('20220810024655_AddingMainMailAddressToProperty','8.0.6'),('20220824154458_AddingExpireDateToChemicalProductProperty','8.0.6'),('20220828101920_AddingSecondaryeFormIdToAreaRule','8.0.6'),('20220905153327_AddingLastFolderNameToChemicalProductProperty','8.0.6'),('20221019143021_AddingEmail','8.0.6'),('20221019143124_AddingEmailDelayed','8.0.6'),('20221023112526_AddingEmailAttachment','8.0.6'),('20221108183349_AddingPriorityToWorkOrderCase','8.0.6'),('20221120065821_AddingTaskManagementEnabledToPropertyWorker','8.0.6'),('20221215120936_AddingStatusToPlanningSite','8.0.6'),('20230129095037_AddingAreaRulePlanningsIdToPlanningSiteVersion','8.0.6'),('20230129100920_AddUseStartDateAsStartOfPeriodToAreaRulePlanning','8.0.6'),('20230202183330_AddFiles','8.0.6'),('20230215171543_ChangeFileHasManyProperties','8.0.6'),('20230407184155_AddEnabledColumnsOnTaskTrackerTable','8.0.6'),('20230420095318_AddingIsDisabledToArea','8.0.6'),('20230703112050_AddingIndexOnCompliancePlanningIdAndDeadline','8.0.6'),('20230727154618_AddLinksAndNewFieldForAreaRulePlanning','8.0.6'),('20230728164447_DeleteNotNeededLinks','8.0.6'),('20230817153730_AddLinkWithItemPlanningTagsAndAreaRulePlanning','8.0.6'),('20230819151348_AddingCreatedInGuideToAreaRule','8.0.6'),('20231228085748_AddingNewAttributesToWorkorderCase','8.0.6'),('20240613043610_AddPinCodeToPropertyWorker','8.0.6');
/*!40000 ALTER TABLE `__EFMigrationsHistory` ENABLE KEYS */;
UNLOCK TABLES;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2024-06-13 12:04:48
