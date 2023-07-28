-- MariaDB dump 10.19  Distrib 10.6.11-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: localhost    Database: 420_eform-backend-configuration-plugin
-- ------------------------------------------------------
-- Server version	10.6.11-MariaDB-0ubuntu0.22.04.1

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
) ENGINE=InnoDB AUTO_INCREMENT=29 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaInitialFields`
--

LOCK TABLES `AreaInitialFields` WRITE;
/*!40000 ALTER TABLE `AreaInitialFields` DISABLE KEYS */;
INSERT INTO `AreaInitialFields` VALUES (1,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,0),(2,'02. Brandudstyr',1,12,3,NULL,NULL,NULL,NULL,2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(3,'03. Kontrol konstruktion',1,12,3,NULL,2,2,NULL,3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(4,'04. Foderindlægssedler',0,1,1,NULL,NULL,NULL,NULL,4,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(5,'05. Stald_klargøring',1,0,NULL,NULL,NULL,NULL,NULL,5,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(6,'06. Siloer',1,1,3,NULL,NULL,NULL,NULL,6,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(7,'07. Rotter',0,1,1,NULL,NULL,NULL,NULL,7,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(8,'08. Luftrensning timer',1,12,3,NULL,NULL,NULL,NULL,8,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(9,'09. Forsuring pH værdi',1,12,3,NULL,NULL,NULL,NULL,9,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(10,'10. Varmepumpe timer og energi',1,12,3,NULL,NULL,NULL,NULL,10,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(11,'11. Varmkilder',0,1,3,NULL,NULL,NULL,NULL,11,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(12,'12. Dieseltank',1,12,3,NULL,NULL,NULL,NULL,12,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(13,'13. APV Medarbejder',0,NULL,NULL,NULL,NULL,NULL,NULL,13,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(14,'14. Maskiner',1,12,3,NULL,NULL,NULL,NULL,14,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(15,'15. Elværktøj',1,12,3,NULL,NULL,NULL,NULL,15,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(16,'16. Stiger',1,12,3,NULL,NULL,NULL,NULL,16,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(17,'17. Brandslukkere',1,12,3,NULL,NULL,NULL,NULL,17,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(18,'18. Alarm',1,1,3,NULL,NULL,NULL,NULL,18,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(19,'19. Ventilation',1,1,3,NULL,NULL,NULL,NULL,19,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(20,'20. Arbejdsopgave udført',1,7,1,NULL,NULL,NULL,NULL,20,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,0),(21,NULL,0,NULL,NULL,NULL,NULL,NULL,NULL,21,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,0),(22,'22. Sigtetest',1,14,1,NULL,NULL,NULL,NULL,22,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(23,NULL,1,NULL,NULL,NULL,NULL,NULL,NULL,26,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(24,NULL,1,NULL,1,NULL,NULL,NULL,NULL,27,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(25,NULL,0,NULL,0,NULL,NULL,NULL,NULL,28,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,0),(26,NULL,0,NULL,0,NULL,NULL,NULL,NULL,29,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,0),(27,'26. Kornlager',1,12,3,1,NULL,NULL,NULL,30,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1),(28,'',1,12,3,1,NULL,NULL,NULL,31,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,1);
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
  `ItemPlanningTagId` int(11) NULL DEFAULT 0,
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
) ENGINE=InnoDB AUTO_INCREMENT=85 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaTranslations`
--

LOCK TABLES `AreaTranslations` WRITE;
/*!40000 ALTER TABLE `AreaTranslations` DISABLE KEYS */;
INSERT INTO `AreaTranslations` VALUES (1,1,'01. Logbøger Miljøledelse','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3xleju932igg',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et fokusområde pr. linje','Fokusområde','Nyt fokusområde'),(2,1,'01. Log books Environmental management','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3xleju932igg',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'An area of focus per line','Area of focus','New area of focus'),(3,1,'01. Logbücher Umweltmanagement','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3xleju932igg',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ein Fokusområde pro Zeile','Fokusbereich','Neues Fokusområde'),(4,2,'02. Beredskab','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.lxbt89gfjmsr',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et beredskabsområde pr. linje','Beredskabsområde','Nyt beredskabsområde'),(5,2,'02. Contingency','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.lxbt89gfjmsr',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One contingency area per line','Contingency area','New contingency area'),(6,2,'02. Kontingenz','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.lxbt89gfjmsr',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ein Kontingenz-Bereich pro Zeile','Kontingenz-Bereich','Neuer Kontingenz-Bereich'),(7,3,'03. Gyllebeholdere','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En gyllebeholder pr. linje','Gyllebeholder','Ny gyllebeholder'),(8,3,'03. Slurry tanks','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One slurry tank per line','Slurry tank','New slurry tank'),(9,3,'03. Gülletanks','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.f8xu36lz5589',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Gülle-Tank pro Zeile','Gülle-Tank','Neue Gülle-Tank'),(10,4,'04. Foderindlægssedler','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.4a0a8zqjbwmq',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En foderblanding pr. linje','Foderblanding','Ny foderblanding'),(11,4,'04. Feeding documentation (kun IE-livestock only)','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.4a0a8zqjbwmq',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one feeding plan per line','Feeding plan','New feeding plan'),(12,4,'04. Fütterungsdokumentation (nur IE Vieh)','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.4a0a8zqjbwmq',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Fütterungsplanung pro Zeile','Fütterungsplanung','Neue Fütterungsplanung'),(13,5,'05. Stalde: Halebid og klargøring','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En stald pr. linje','Stald','Ny stald til klargøring'),(14,5,'05. Stables: Tail bite and preparation','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One stable per line','Stable','New stable'),(15,5,'05. Ställe: Schwanzbiss und Vorbereitung','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.2ganay44a9yy',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Ställe pro Zeile','Ställe','Neue Ställe'),(16,6,'06. Fodersiloer','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.9rbo49l8hwn1',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En fodersilo pr. linje','Fodersilo','Ny fodersilo'),(17,6,'06. Silos','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.9rbo49l8hwn1',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one silo per line','Silo','New silo'),(18,6,'06. Silos','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.9rbo49l8hwn1',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Silo pro Zeile','Silo','Neue Silo'),(19,7,'07. Skadedyrsbekæmpelse','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iljrzeutkuw4',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et kontrolområde pr. linje','Kontrolområde','Nyt kontrolområde'),(20,7,'07. Pest control','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iljrzeutkuw4',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one pest control area per line','Pest control area','New pest control area'),(21,7,'07. Schädlingsbekämpfung','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iljrzeutkuw4',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Schädlingsbekämpfungsgebiet pro Zeile','Schädlingsbekämpfungsgebiet','Neue Schädlingsbekämpfungsgebiet'),(22,8,'08. Luftrensning','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.q3puu5rb21t5',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et kontrolområde pr. linje','Kontrolområde','Nyt kontrolområde'),(23,8,'08. Aircleaning','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.q3puu5rb21t5',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one air cleaning area per line','Aircleaning area','New air cleaning area'),(24,8,'08. Luftreinigung','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.q3puu5rb21t5',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Luftreinigungsgebiet pro Zeile','Luftreinigungsgebiet','Neue Luftreinigungsgebiet'),(25,9,'09. Forsuring','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdavny7gjz0n',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et kontrolområde pr. linje','Kontrolområde','Nyt kontrolområde'),(26,9,'09. Acidification','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdavny7gjz0n',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one acidification area per line','Acidification area','New acidification area'),(27,9,'09. Ansäuerung','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdavny7gjz0n',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Ansäuerungsgebiet pro Zeile','Ansäuerungsgebiet','Neue Ansäuerungsgebiet'),(28,10,'10. Varmepumper','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.dxezyj6gry62',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En varmepumpe pr. linje','Varmepumpe','Ny varmepumpe'),(29,10,'10. Heat pumps','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.dxezyj6gry62',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one heat pump per line','Heatpump','New heatpump'),(30,10,'10. Wärmepumpen','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.dxezyj6gry62',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Wärmepumpe pro Zeile','Wärmepumpen','Neue Wärmepumpe'),(31,11,'11. Varmekilder','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.er2v0a3yxqzu',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En varmekilde pr. linje','Varmekilde','Ny varmekilde'),(32,11,'11. Heat sources','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.er2v0a3yxqzu',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one heat source per line','Heat source','New heat source'),(33,11,'11. Wärmequellen','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.er2v0a3yxqzu',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Wärmequelle pro Zeile','Wärmequelle','Neue Wärmequelle'),(34,12,'12. Miljøfarlige stoffer','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.ocy0eycm3hu7',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et kontrolområde pr. linje','Kontrolområde','Nyt kontrolområde'),(35,12,'12. Environmentally hazardous substances','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.ocy0eycm3hu7',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one control area per line','Control area','New control area'),(36,12,'12. Umweltgefährdende Stoffe','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.ocy0eycm3hu7',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Umweltgefährdende Stoffe pro Zeile','Umweltgefährdende Stoffe','Neue Umweltgefährdende Stoffe'),(37,13,'13. APV Landbrug','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.o3ig9krjpdcb',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(38,13,'13. APV Agriculture','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.o3ig9krjpdcb',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(39,13,'13. APV Landwirtschaft','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.o3ig9krjpdcb',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(40,14,'14. Maskiner','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5t8ueh77brvx',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En maskine pr. linje','Maskine','Ny maskine'),(41,14,'14. Machines','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5t8ueh77brvx',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one Machine per line','Machine','New Machine'),(42,14,'14. Machinen','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5t8ueh77brvx',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Maschine pro Zeile','Maschine','Neue Maschine'),(43,15,'15. Elværktøj','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3aqslige0sx8',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et elværktøj pr. linje','Elværktøj','Nyt elværktøj'),(44,15,'15. Inspection of power tools','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3aqslige0sx8',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one power tool per line','Power tool','New power tool'),(45,15,'15. Inspektion von Elektrowerkzeugen','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.3aqslige0sx8',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Elektrowerkzeug pro Zeile','Elektrowerkzeug','Neues Elektrowerkzeug'),(46,16,'16. Stiger','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.201m31f6b76t',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En stige pr. linje','Stige','Ny stige'),(47,16,'16. Ladders','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.201m31f6b76t',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one ladder per line','Ladder','New ladder'),(48,16,'16. Leitern','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.201m31f6b76t',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Leiter pro Zeile','Leiter','Neuer Leiter'),(49,17,'17. Brandslukkere','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5f3qhcjuqopu',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En brandslukker pr. linje','Brandslukker','Ny brandslukker'),(50,17,'17. Fire extinguishers','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5f3qhcjuqopu',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one fire extinguisher per line','Fire extinguisher','New fire extinguisher'),(51,17,'17. Feuerlöscher','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5f3qhcjuqopu',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Feuerlöscher pro Zeile','Feuerlöscher','Neuer Feuerlöscher'),(52,18,'18. Alarm','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdl5bg82luo9',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En alarm pr. linje','Alarm','Ny alarm'),(53,18,'18. Alarm','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdl5bg82luo9',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one alarm per line','Alarm','New alarm'),(54,18,'18. Alarm','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xdl5bg82luo9',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Alarm pro Zeile','Alarm','Neuer Alarm'),(55,19,'19. Ventilation','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.122yhikalodh',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En ventilation pr. linje','Ventilation','Ny ventilation'),(56,19,'19. Ventilation','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.122yhikalodh',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one ventilation per line','Ventilation','New ventilation'),(57,19,'19. Belüftung','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.122yhikalodh',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Belüftung pro Zeile','Belüftung','Neue Belüftung'),(58,20,'20. Ugentlige rutineopgaver','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.7sb10z3swexo',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En rutineopgave pr. linje','Èn rutineopgave pr. linje','Ny rutineopgave'),(59,20,'20. Weekly routine tasks','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.7sb10z3swexo',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one routine task per line','Routine task','New routine task'),(60,20,'20. Wöchentliche Routineaufgaben','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.7sb10z3swexo',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur eine Wöchentliche Routineaufgaben pro Zeile','Wöchentliche Routineaufgaben','Neue Wöchentliche Routineaufgaben'),(61,21,'21. DANISH Standard','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iw36kvdmxgi8',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(62,21,'21. DANISH Standard','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iw36kvdmxgi8',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(63,21,'21. DANISH Standard','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.iw36kvdmxgi8',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(64,22,'22. Sigtetest','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xawyj9b4y3rq',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'En foderblanding pr. linje','Foderblanding','Ny foderblanding'),(65,22,'22. Sieve test','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xawyj9b4y3rq',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One sieve test per line','Sieve test','New sieve test'),(66,22,'22. Testen mit Sieb','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.xawyj9b4y3rq',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ein Sieb-Test pro Zeile','Sieb-Test','Neuer Sieb-Test'),(67,26,'99. Diverse','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5cyvenoqt2qk',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Kun et kontrolområde pr. linje','Kontrolområde','Nyt kontrolområde'),(68,26,'99. Miscellaneous','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5cyvenoqt2qk',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Only one control area per line','Control area','New control area'),(69,26,'99. Sonstig','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.5cyvenoqt2qk',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Kontroll-Bereich pro Zeile','Kontroll-Bereich','Neuer Kontroll-Bereich'),(70,27,'24. IE-indberetning','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Se krav i Miljøgodkendelse','','Vælg indberetningsområder'),(71,27,'24. IE Reporting','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'See requirements in Environment Approval','','Choose reporting areas'),(72,27,'24. IE-Berichterstattung','https://www.microting.dk/eform/landbrug/omr%C3%A5der#h.8kzwebwrj4gz',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Siehe Anforderungen in Umweltzulassung','','Berichtsgebiete auswählen'),(73,28,'25. KemiKontrol','',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(74,28,'25. Chemistry Control','',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(75,28,'25. Chemiekontrolle','',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(76,29,'00. Aflæsninger, målinger, forbrug og fækale uheld','',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et bassin pr linie','',''),(77,29,'00. Readings, measurements, consumption and fecal accidents','',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(78,29,'00. Messwerte, Messungen, Verbrauch und Fäkalunfälle','',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','',''),(79,30,'26. Kornlager','',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et lager pr. linje','Kornlager','Nyt kornlager'),(80,30,'26. Grain store','',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'One warehouse per line','Grain store','New grain store'),(81,30,'26. Getreidelagerung','',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Nur ein Lager pro Linie','Getreidelagerung','Neues Getreidelagerung'),(82,31,'00. Logbøger','',1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Et fokusområde pr. linje','Fokusområde','Nyt fokusområde'),(83,31,'01. Log books','',2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'An area of focus per line','Area of focus','New area of focus'),(84,31,'01. Logbücher','',3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ein Fokusområde pro Zeile','Fokusbereich','Neues Fokusområde');
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
) ENGINE=InnoDB AUTO_INCREMENT=29 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AreaVersions`
--

LOCK TABLES `AreaVersions` WRITE;
/*!40000 ALTER TABLE `AreaVersions` DISABLE KEYS */;
INSERT INTO `AreaVersions` VALUES (1,1,1,1,'2023-02-07 12:00:45.113459','2023-02-07 12:00:45.113461','created',0,0,1,1,0),(2,1,2,2,'2023-02-07 12:00:45.648771','2023-02-07 12:00:45.648773','created',0,0,1,1,0),(3,2,3,3,'2023-02-07 12:00:45.912596','2023-02-07 12:00:45.912598','created',0,0,1,1,0),(4,1,4,4,'2023-02-07 12:00:46.156785','2023-02-07 12:00:46.156787','created',0,0,1,1,0),(5,3,5,5,'2023-02-07 12:00:46.232252','2023-02-07 12:00:46.232254','created',0,0,1,1,0),(6,1,6,6,'2023-02-07 12:00:46.392780','2023-02-07 12:00:46.392782','created',0,0,1,1,0),(7,1,7,7,'2023-02-07 12:00:46.512702','2023-02-07 12:00:46.512704','created',0,0,1,1,0),(8,1,8,8,'2023-02-07 12:00:46.589626','2023-02-07 12:00:46.589627','created',0,0,1,1,0),(9,1,9,9,'2023-02-07 12:00:46.827823','2023-02-07 12:00:46.827825','created',0,0,1,1,0),(10,6,10,10,'2023-02-07 12:00:47.119556','2023-02-07 12:00:47.119558','created',0,0,1,1,0),(11,1,11,11,'2023-02-07 12:00:47.319049','2023-02-07 12:00:47.319051','created',0,0,1,1,0),(12,1,12,12,'2023-02-07 12:00:47.621012','2023-02-07 12:00:47.621014','created',0,0,1,1,0),(13,4,13,13,'2023-02-07 12:00:47.932334','2023-02-07 12:00:47.932336','created',0,0,1,1,0),(14,1,14,14,'2023-02-07 12:00:48.342754','2023-02-07 12:00:48.342756','created',0,0,1,1,0),(15,1,15,15,'2023-02-07 12:00:48.663112','2023-02-07 12:00:48.663113','created',0,0,1,1,0),(16,1,16,16,'2023-02-07 12:00:48.985374','2023-02-07 12:00:48.985375','created',0,0,1,1,0),(17,1,17,17,'2023-02-07 12:00:49.205002','2023-02-07 12:00:49.205004','created',0,0,1,1,0),(18,1,18,18,'2023-02-07 12:00:49.514299','2023-02-07 12:00:49.514301','created',0,0,1,1,0),(19,1,19,19,'2023-02-07 12:00:49.719359','2023-02-07 12:00:49.719361','created',0,0,1,1,0),(20,5,20,20,'2023-02-07 12:00:49.959564','2023-02-07 12:00:49.959566','created',0,0,1,1,0),(21,4,21,21,'2023-02-07 12:00:50.189077','2023-02-07 12:00:50.189079','created',0,0,1,1,0),(22,1,22,22,'2023-02-07 12:00:50.423991','2023-02-07 12:00:50.423993','created',0,0,1,1,0),(23,1,23,26,'2023-02-07 12:00:50.701901','2023-02-07 12:00:50.701903','created',0,0,1,1,0),(24,8,24,27,'2023-02-07 12:00:50.982865','2023-02-07 12:00:50.982867','created',0,0,1,1,0),(25,9,25,28,'2023-02-07 12:00:51.256696','2023-02-07 12:00:51.256698','created',0,0,1,1,0),(26,10,26,29,'2023-02-07 12:00:51.519528','2023-02-07 12:00:51.519530','created',0,0,1,0,0),(27,1,27,30,'2023-02-07 12:00:51.889097','2023-02-07 12:00:51.889098','created',0,0,1,1,0),(28,1,28,31,'2023-02-07 12:00:52.270188','2023-02-07 12:00:52.270190','created',0,0,1,1,0);
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
INSERT INTO `Areas` VALUES (1,1,1,'2023-02-07 12:00:45.113459','2023-02-07 12:00:45.113461','created',0,0,1,1,0),(2,1,2,'2023-02-07 12:00:45.648771','2023-02-07 12:00:45.648773','created',0,0,1,1,0),(3,2,3,'2023-02-07 12:00:45.912596','2023-02-07 12:00:45.912598','created',0,0,1,1,0),(4,1,4,'2023-02-07 12:00:46.156785','2023-02-07 12:00:46.156787','created',0,0,1,1,0),(5,3,5,'2023-02-07 12:00:46.232252','2023-02-07 12:00:46.232254','created',0,0,1,1,0),(6,1,6,'2023-02-07 12:00:46.392780','2023-02-07 12:00:46.392782','created',0,0,1,1,0),(7,1,7,'2023-02-07 12:00:46.512702','2023-02-07 12:00:46.512704','created',0,0,1,1,0),(8,1,8,'2023-02-07 12:00:46.589626','2023-02-07 12:00:46.589627','created',0,0,1,1,0),(9,1,9,'2023-02-07 12:00:46.827823','2023-02-07 12:00:46.827825','created',0,0,1,1,0),(10,6,10,'2023-02-07 12:00:47.119556','2023-02-07 12:00:47.119558','created',0,0,1,1,0),(11,1,11,'2023-02-07 12:00:47.319049','2023-02-07 12:00:47.319051','created',0,0,1,1,0),(12,1,12,'2023-02-07 12:00:47.621012','2023-02-07 12:00:47.621014','created',0,0,1,1,0),(13,4,13,'2023-02-07 12:00:47.932334','2023-02-07 12:00:47.932336','created',0,0,1,1,0),(14,1,14,'2023-02-07 12:00:48.342754','2023-02-07 12:00:48.342756','created',0,0,1,1,0),(15,1,15,'2023-02-07 12:00:48.663112','2023-02-07 12:00:48.663113','created',0,0,1,1,0),(16,1,16,'2023-02-07 12:00:48.985374','2023-02-07 12:00:48.985375','created',0,0,1,1,0),(17,1,17,'2023-02-07 12:00:49.205002','2023-02-07 12:00:49.205004','created',0,0,1,1,0),(18,1,18,'2023-02-07 12:00:49.514299','2023-02-07 12:00:49.514301','created',0,0,1,1,0),(19,1,19,'2023-02-07 12:00:49.719359','2023-02-07 12:00:49.719361','created',0,0,1,1,0),(20,5,20,'2023-02-07 12:00:49.959564','2023-02-07 12:00:49.959566','created',0,0,1,1,0),(21,4,21,'2023-02-07 12:00:50.189077','2023-02-07 12:00:50.189079','created',0,0,1,1,0),(22,1,22,'2023-02-07 12:00:50.423991','2023-02-07 12:00:50.423993','created',0,0,1,1,0),(26,1,23,'2023-02-07 12:00:50.701901','2023-02-07 12:00:50.701903','created',0,0,1,1,0),(27,8,24,'2023-02-07 12:00:50.982865','2023-02-07 12:00:50.982867','created',0,0,1,1,0),(28,9,25,'2023-02-07 12:00:51.256696','2023-02-07 12:00:51.256698','created',0,0,1,1,0),(29,10,26,'2023-02-07 12:00:51.519528','2023-02-07 12:00:51.519530','created',0,0,1,0,0),(30,1,27,'2023-02-07 12:00:51.889097','2023-02-07 12:00:51.889098','created',0,0,1,1,0),(31,1,28,'2023-02-07 12:00:52.270188','2023-02-07 12:00:52.270190','created',0,0,1,1,0);
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
  PRIMARY KEY (`Id`)
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
INSERT INTO `PluginConfigurationValues` VALUES (1,'BackendConfigurationSettings:ReportSubHeaderName','','2023-02-07 11:51:47.345131','2023-02-07 11:51:47.345414','created',1,0,1),(2,'BackendConfigurationSettings:ReportHeaderName','','2023-02-07 11:51:47.518100','2023-02-07 11:51:47.518102','created',1,0,1),(3,'BackendConfigurationSettings:MaxChrNumbers','10','2023-02-07 11:51:47.526932','2023-02-07 11:51:47.526933','created',1,0,1),(4,'BackendConfigurationSettings:MaxCvrNumbers','10','2023-02-07 11:51:47.531229','2023-02-07 11:51:47.531231','created',1,0,1);
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
INSERT INTO `PluginGroupPermissionVersions` VALUES (1,1,1,1,1,'2023-02-07 11:51:47.880385','2023-02-07 11:51:47.880387','created',0,0,1),(2,1,5,1,2,'2023-02-07 11:51:48.671784','2023-02-07 11:51:48.671786','created',0,0,1),(3,1,6,1,3,'2023-02-07 11:51:48.710543','2023-02-07 11:51:48.710545','created',0,0,1),(4,1,2,1,4,'2023-02-07 11:51:48.783400','2023-02-07 11:51:48.783402','created',0,0,1),(5,1,3,1,5,'2023-02-07 11:51:48.875248','2023-02-07 11:51:48.875251','created',0,0,1),(6,1,4,1,6,'2023-02-07 11:51:48.975399','2023-02-07 11:51:48.975402','created',0,0,1),(7,1,7,1,7,'2023-02-07 11:51:49.058022','2023-02-07 11:51:49.058025','created',0,0,1),(8,1,8,1,8,'2023-02-07 11:51:49.139887','2023-02-07 11:51:49.139889','created',0,0,1);
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
INSERT INTO `PluginGroupPermissions` VALUES (1,1,1,1,'2023-02-07 11:51:47.880385','2023-02-07 11:51:47.880387','created',0,0,1),(2,1,5,1,'2023-02-07 11:51:48.671784','2023-02-07 11:51:48.671786','created',0,0,1),(3,1,6,1,'2023-02-07 11:51:48.710543','2023-02-07 11:51:48.710545','created',0,0,1),(4,1,2,1,'2023-02-07 11:51:48.783400','2023-02-07 11:51:48.783402','created',0,0,1),(5,1,3,1,'2023-02-07 11:51:48.875248','2023-02-07 11:51:48.875251','created',0,0,1),(6,1,4,1,'2023-02-07 11:51:48.975399','2023-02-07 11:51:48.975402','created',0,0,1),(7,1,7,1,'2023-02-07 11:51:49.058022','2023-02-07 11:51:49.058025','created',0,0,1),(8,1,8,1,'2023-02-07 11:51:49.139887','2023-02-07 11:51:49.139889','created',0,0,1);
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
INSERT INTO `PluginPermissions` VALUES (1,'Access BackendConfiguration Plugin','backend_configuration_plugin_access','2023-02-07 11:51:47.539731',NULL,'created',1,0,1),(2,'Create property','properties_create','2023-02-07 11:51:47.557995',NULL,'created',1,0,1),(3,'Get properties','properties_get','2023-02-07 11:51:47.559517',NULL,'created',1,0,1),(4,'Edit property','property_edit','2023-02-07 11:51:47.560918',NULL,'created',1,0,1),(5,'Enable chemical management','chemical_management_enable','2023-02-07 11:51:47.562209',NULL,'created',1,0,1),(6,'Enable document management','document_management_enable','2023-02-07 11:51:47.563479',NULL,'created',1,0,1),(7,'Enable task management','task_management_enable','2023-02-07 11:51:47.564613',NULL,'created',1,0,1),(8,'Enable time registration','time_registration_enable','2023-02-07 11:51:47.565664',NULL,'created',1,0,1);
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
INSERT INTO `__EFMigrationsHistory` VALUES ('20211025142151_InitialCreate','7.0.2'),('20211110114709_AddingCVRToProperties','7.0.2'),('20220101103446_AddingPropertyIdToAreaRulePlanning','7.0.2'),('20220101111025_AddingAreaIdToAreaRulePlanning','7.0.2'),('20220103152353_AddingComplianceStatusToProperty','7.0.2'),('20220105092557_CreateComplianceClass','7.0.2'),('20220105135743_ChangingAreaIdToInt','7.0.2'),('20220105140315_AddingMoreAttributesToCompliance','7.0.2'),('20220106165734_AddingComplianceStatus30ToProperties','7.0.2'),('20220127172400_AddComplianceEnabledProp','7.0.2'),('20220208172326_AddingPlanningCaseSiteIdAndCheckListSiteId','7.0.2'),('20220210171743_AddWorkorderFlow','7.0.2'),('20220216140313_AddEntitySelectListsForProperty','7.0.2'),('20220217112234_AddingAdditionalFolderIds','7.0.2'),('20220217133829_AddingEntityItemIdToPropertyWorker','7.0.2'),('20220217165554_AddingAttributesToWorkorderCase','7.0.2'),('20220217174831_AddingCreatedByNameToWorkorderCase','7.0.2'),('20220217182010_AddingCreatedByTextToWorkorderCase','7.0.2'),('20220217183352_AddingDescriptionToWorkorderCase','7.0.2'),('20220217184356_AddingCaseInitiatedToWorkorderCase','7.0.2'),('20220217193352_CreateWorkorderCaseImage','7.0.2'),('20220321151057_AddFieldsPlaceholderAndInfoForAreas','7.0.2'),('20220405183346_AddingNewItemName','7.0.2'),('20220417055742_AddingAssignmentAttributesToWorkorderCases','7.0.2'),('20220425054126_AddRepeatTypeToAreaRule','7.0.2'),('20220425151849_AddingMoreAttributesToAreaRule','7.0.2'),('20220426190544_AddingLeadingCaseToWorkorderCase','7.0.2'),('20220603160201_AddingEntitySearchListChemicalsToProperty','7.0.2'),('20220606072856_AddingEntitySearchListChemicalRegNosToProperty','7.0.2'),('20220607114609_CreatingChemicalProductProperty','7.0.2'),('20220609162637_AddingIndustryCodeIsFarmToProperty','7.0.2'),('20220610080134_FixingIndustryCodeTobeString','7.0.2'),('20220612043907_AddingIsFarmToArea','7.0.2'),('20220612164010_AddingPoolHours','7.0.2'),('20220613055856_AddingMorePoolHourTables','7.0.2'),('20220613132009_AllowingItemPlanningIdToBeNull','7.0.2'),('20220614143506_AddingMoreIdsToPlanningSites','7.0.2'),('20220616160212_AddingEntitySearchListPoolWorkers','7.0.2'),('20220621070937_AddingFolderIdToPoolRegistrations','7.0.2'),('20220623095747_ChangingAttributeTypes','7.0.2'),('20220623173220_CreatingPoolHistorySite','7.0.2'),('20220623184950_AllowingValuesToBeNull','7.0.2'),('20220630080523_AddingDoneAtToPoolHourResult','7.0.2'),('20220630161000_AddingSdkCaseIdToChemicalProductProperty','7.0.2'),('20220703144301_AddingChemicalProductPropertySite','7.0.2'),('20220705160545_AddingEntitySelectListChemicalAreasToProperty','7.0.2'),('20220724155019_AddingLocationsToChemicalProductProperty','7.0.2'),('20220806155246_AddingLanguageIdAndSdkSiteIdToChemicalProductProperty','7.0.2'),('20220810024655_AddingMainMailAddressToProperty','7.0.2'),('20220824154458_AddingExpireDateToChemicalProductProperty','7.0.2'),('20220828101920_AddingSecondaryeFormIdToAreaRule','7.0.2'),('20220905153327_AddingLastFolderNameToChemicalProductProperty','7.0.2'),('20221019143021_AddingEmail','7.0.2'),('20221019143124_AddingEmailDelayed','7.0.2'),('20221023112526_AddingEmailAttachment','7.0.2'),('20221108183349_AddingPriorityToWorkOrderCase','7.0.2'),('20221120065821_AddingTaskManagementEnabledToPropertyWorker','7.0.2'),('20221215120936_AddingStatusToPlanningSite','7.0.2'),('20230129095037_AddingAreaRulePlanningsIdToPlanningSiteVersion','7.0.2'),('20230129100920_AddUseStartDateAsStartOfPeriodToAreaRulePlanning','7.0.2');
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

-- Dump completed on 2023-02-08 16:06:06
