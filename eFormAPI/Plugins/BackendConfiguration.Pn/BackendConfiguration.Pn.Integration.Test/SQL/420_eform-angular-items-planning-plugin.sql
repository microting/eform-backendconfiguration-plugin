# noinspection SqlNoDataSourceInspectionForFile

-- MariaDB dump 10.19  Distrib 10.6.11-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: localhost    Database: 420_eform-angular-items-planning-plugin
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
-- Table structure for table `Languages`
--

DROP TABLE IF EXISTS `Languages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Languages` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
  `LanguageCode` longtext DEFAULT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Languages`
--

LOCK TABLES `Languages` WRITE;
/*!40000 ALTER TABLE `Languages` DISABLE KEYS */;
INSERT INTO `Languages` VALUES (1,1,'created','2023-02-07 11:45:25.000000','2023-02-07 11:45:25.000000','Danish','da',0),(2,1,'created','2023-02-07 11:45:25.000000','2023-02-07 11:45:25.000000','English','en-US',0),(3,1,'created','2023-02-07 11:45:25.000000','2023-02-07 11:45:25.000000','German','de-DE',0);
/*!40000 ALTER TABLE `Languages` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningCaseSiteVersions`
--

DROP TABLE IF EXISTS `PlanningCaseSiteVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningCaseSiteVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `MicrotingSdkSiteId` int(11) NOT NULL,
  `MicrotingSdkeFormId` int(11) NOT NULL,
  `Status` int(11) NOT NULL,
  `FieldStatus` longtext DEFAULT NULL,
  `MicrotingSdkCaseId` int(11) NOT NULL,
  `MicrotingSdkCaseDoneAt` datetime(6) DEFAULT NULL,
  `NumberOfImages` int(11) NOT NULL,
  `Comment` longtext DEFAULT NULL,
  `Location` longtext DEFAULT NULL,
  `PlanningCaseSiteId` int(11) NOT NULL,
  `SdkFieldValue1` longtext DEFAULT NULL,
  `SdkFieldValue2` longtext DEFAULT NULL,
  `SdkFieldValue3` longtext DEFAULT NULL,
  `SdkFieldValue4` longtext DEFAULT NULL,
  `SdkFieldValue5` longtext DEFAULT NULL,
  `SdkFieldValue6` longtext DEFAULT NULL,
  `SdkFieldValue7` longtext DEFAULT NULL,
  `SdkFieldValue8` longtext DEFAULT NULL,
  `SdkFieldValue9` longtext DEFAULT NULL,
  `SdkFieldValue10` longtext DEFAULT NULL,
  `DoneByUserId` int(11) NOT NULL,
  `DoneByUserName` longtext DEFAULT NULL,
  `PlanningCaseId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL DEFAULT 0,
  `MicrotingCheckListSitId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningCaseSiteVersions`
--

LOCK TABLES `PlanningCaseSiteVersions` WRITE;
/*!40000 ALTER TABLE `PlanningCaseSiteVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningCaseSiteVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningCaseSites`
--

DROP TABLE IF EXISTS `PlanningCaseSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningCaseSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `MicrotingSdkSiteId` int(11) NOT NULL,
  `MicrotingSdkeFormId` int(11) NOT NULL,
  `Status` int(11) NOT NULL,
  `FieldStatus` longtext DEFAULT NULL,
  `MicrotingSdkCaseId` int(11) NOT NULL,
  `MicrotingSdkCaseDoneAt` datetime(6) DEFAULT NULL,
  `NumberOfImages` int(11) NOT NULL,
  `Comment` longtext DEFAULT NULL,
  `Location` longtext DEFAULT NULL,
  `SdkFieldValue1` longtext DEFAULT NULL,
  `SdkFieldValue2` longtext DEFAULT NULL,
  `SdkFieldValue3` longtext DEFAULT NULL,
  `SdkFieldValue4` longtext DEFAULT NULL,
  `SdkFieldValue5` longtext DEFAULT NULL,
  `SdkFieldValue6` longtext DEFAULT NULL,
  `SdkFieldValue7` longtext DEFAULT NULL,
  `SdkFieldValue8` longtext DEFAULT NULL,
  `SdkFieldValue9` longtext DEFAULT NULL,
  `SdkFieldValue10` longtext DEFAULT NULL,
  `DoneByUserId` int(11) NOT NULL,
  `DoneByUserName` longtext DEFAULT NULL,
  `PlanningCaseId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL DEFAULT 0,
  `MicrotingCheckListSitId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningCaseSites`
--

LOCK TABLES `PlanningCaseSites` WRITE;
/*!40000 ALTER TABLE `PlanningCaseSites` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningCaseSites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningCaseVersions`
--

DROP TABLE IF EXISTS `PlanningCaseVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningCaseVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `MicrotingSdkSiteId` int(11) NOT NULL,
  `MicrotingSdkeFormId` int(11) NOT NULL,
  `Status` int(11) NOT NULL,
  `FieldStatus` longtext DEFAULT NULL,
  `MicrotingSdkCaseId` int(11) NOT NULL,
  `MicrotingSdkCaseDoneAt` datetime(6) DEFAULT NULL,
  `NumberOfImages` int(11) NOT NULL,
  `Comment` longtext DEFAULT NULL,
  `Location` longtext DEFAULT NULL,
  `PlanningCaseId` int(11) NOT NULL,
  `SdkFieldValue1` longtext DEFAULT NULL,
  `SdkFieldValue2` longtext DEFAULT NULL,
  `SdkFieldValue3` longtext DEFAULT NULL,
  `SdkFieldValue4` longtext DEFAULT NULL,
  `SdkFieldValue5` longtext DEFAULT NULL,
  `SdkFieldValue6` longtext DEFAULT NULL,
  `SdkFieldValue7` longtext DEFAULT NULL,
  `SdkFieldValue8` longtext DEFAULT NULL,
  `SdkFieldValue9` longtext DEFAULT NULL,
  `SdkFieldValue10` longtext DEFAULT NULL,
  `DoneByUserId` int(11) NOT NULL,
  `DoneByUserName` longtext DEFAULT NULL,
  `PlanningId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningCaseVersions`
--

LOCK TABLES `PlanningCaseVersions` WRITE;
/*!40000 ALTER TABLE `PlanningCaseVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningCaseVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningCases`
--

DROP TABLE IF EXISTS `PlanningCases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningCases` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `MicrotingSdkSiteId` int(11) NOT NULL,
  `MicrotingSdkeFormId` int(11) NOT NULL,
  `Status` int(11) NOT NULL,
  `FieldStatus` longtext DEFAULT NULL,
  `MicrotingSdkCaseId` int(11) NOT NULL,
  `MicrotingSdkCaseDoneAt` datetime(6) DEFAULT NULL,
  `NumberOfImages` int(11) NOT NULL,
  `Comment` longtext DEFAULT NULL,
  `Location` longtext DEFAULT NULL,
  `SdkFieldValue1` longtext DEFAULT NULL,
  `SdkFieldValue2` longtext DEFAULT NULL,
  `SdkFieldValue3` longtext DEFAULT NULL,
  `SdkFieldValue4` longtext DEFAULT NULL,
  `SdkFieldValue5` longtext DEFAULT NULL,
  `SdkFieldValue6` longtext DEFAULT NULL,
  `SdkFieldValue7` longtext DEFAULT NULL,
  `SdkFieldValue8` longtext DEFAULT NULL,
  `SdkFieldValue9` longtext DEFAULT NULL,
  `SdkFieldValue10` longtext DEFAULT NULL,
  `DoneByUserId` int(11) NOT NULL,
  `DoneByUserName` longtext DEFAULT NULL,
  `PlanningId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanningCases_PlanningId` (`PlanningId`),
  CONSTRAINT `FK_PlanningCases_Plannings_PlanningId` FOREIGN KEY (`PlanningId`) REFERENCES `Plannings` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningCases`
--

LOCK TABLES `PlanningCases` WRITE;
/*!40000 ALTER TABLE `PlanningCases` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningCases` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningNameTranslation`
--

DROP TABLE IF EXISTS `PlanningNameTranslation`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningNameTranslation` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` varchar(250) DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanningNameTranslation_PlanningId` (`PlanningId`),
  KEY `IX_PlanningNameTranslation_LanguageId` (`LanguageId`),
  CONSTRAINT `FK_PlanningNameTranslation_Languages_LanguageId` FOREIGN KEY (`LanguageId`) REFERENCES `Languages` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_PlanningNameTranslation_Plannings_PlanningId` FOREIGN KEY (`PlanningId`) REFERENCES `Plannings` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningNameTranslation`
--

LOCK TABLES `PlanningNameTranslation` WRITE;
/*!40000 ALTER TABLE `PlanningNameTranslation` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningNameTranslation` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningNameTranslationVersions`
--

DROP TABLE IF EXISTS `PlanningNameTranslationVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningNameTranslationVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` varchar(250) DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `PlanningNameTranslationId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanningNameTranslationVersions_PlanningNameTranslationId` (`PlanningNameTranslationId`),
  CONSTRAINT `FK_PlanningNameTranslationVersions_PlanningNameTranslation_Plan~` FOREIGN KEY (`PlanningNameTranslationId`) REFERENCES `PlanningNameTranslation` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningNameTranslationVersions`
--

LOCK TABLES `PlanningNameTranslationVersions` WRITE;
/*!40000 ALTER TABLE `PlanningNameTranslationVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningNameTranslationVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningSiteVersions`
--

DROP TABLE IF EXISTS `PlanningSiteVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningSiteVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `SiteId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `PlanningSiteId` int(11) NOT NULL,
  `LastExecutedTime` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningSiteVersions`
--

LOCK TABLES `PlanningSiteVersions` WRITE;
/*!40000 ALTER TABLE `PlanningSiteVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningSiteVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningSites`
--

DROP TABLE IF EXISTS `PlanningSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `SiteId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `LastExecutedTime` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanningSites_PlanningId` (`PlanningId`),
  CONSTRAINT `FK_PlanningSites_Plannings_PlanningId` FOREIGN KEY (`PlanningId`) REFERENCES `Plannings` (`Id`) ON DELETE CASCADE
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
-- Table structure for table `PlanningTagVersions`
--

DROP TABLE IF EXISTS `PlanningTagVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningTagVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
  `PlanningTagId` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=29 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningTagVersions`
--

LOCK TABLES `PlanningTagVersions` WRITE;
/*!40000 ALTER TABLE `PlanningTagVersions` DISABLE KEYS */;
INSERT INTO `PlanningTagVersions` VALUES (1,1,'created','2023-02-07 12:00:44.019218','2023-02-07 12:00:44.019219','01. Logbøger Miljøledelse',1),(2,1,'created','2023-02-07 12:00:45.514535','2023-02-07 12:00:45.514537','02. Beredskab',2),(3,1,'created','2023-02-07 12:00:45.806359','2023-02-07 12:00:45.806361','03. Gyllebeholdere',3),(4,1,'created','2023-02-07 12:00:46.093269','2023-02-07 12:00:46.093270','04. Foderindlægssedler',4),(5,1,'created','2023-02-07 12:00:46.211354','2023-02-07 12:00:46.211356','05. Stalde: Halebid og klargøring',5),(6,1,'created','2023-02-07 12:00:46.328945','2023-02-07 12:00:46.328947','06. Fodersiloer',6),(7,1,'created','2023-02-07 12:00:46.426821','2023-02-07 12:00:46.426823','07. Skadedyrsbekæmpelse',7),(8,1,'created','2023-02-07 12:00:46.549098','2023-02-07 12:00:46.549100','08. Luftrensning',8),(9,1,'created','2023-02-07 12:00:46.764716','2023-02-07 12:00:46.764717','09. Forsuring',9),(10,1,'created','2023-02-07 12:00:46.971529','2023-02-07 12:00:46.971531','10. Varmepumper',10),(11,1,'created','2023-02-07 12:00:47.230176','2023-02-07 12:00:47.230178','11. Varmekilder',11),(12,1,'created','2023-02-07 12:00:47.490283','2023-02-07 12:00:47.490285','12. Miljøfarlige stoffer',12),(13,1,'created','2023-02-07 12:00:47.798598','2023-02-07 12:00:47.798600','13. APV Landbrug',13),(14,1,'created','2023-02-07 12:00:48.127834','2023-02-07 12:00:48.127836','14. Maskiner',14),(15,1,'created','2023-02-07 12:00:48.533737','2023-02-07 12:00:48.533739','15. Elværktøj',15),(16,1,'created','2023-02-07 12:00:48.831948','2023-02-07 12:00:48.831951','16. Stiger',16),(17,1,'created','2023-02-07 12:00:49.094203','2023-02-07 12:00:49.094205','17. Brandslukkere',17),(18,1,'created','2023-02-07 12:00:49.402768','2023-02-07 12:00:49.402770','18. Alarm',18),(19,1,'created','2023-02-07 12:00:49.605971','2023-02-07 12:00:49.605973','19. Ventilation',19),(20,1,'created','2023-02-07 12:00:49.871033','2023-02-07 12:00:49.871034','20. Ugentlige rutineopgaver',20),(21,1,'created','2023-02-07 12:00:50.095846','2023-02-07 12:00:50.095850','21. DANISH Standard',21),(22,1,'created','2023-02-07 12:00:50.336715','2023-02-07 12:00:50.336717','22. Sigtetest',22),(23,1,'created','2023-02-07 12:00:50.572350','2023-02-07 12:00:50.572352','99. Diverse',23),(24,1,'created','2023-02-07 12:00:50.871320','2023-02-07 12:00:50.871322','24. IE-indberetning',24),(25,1,'created','2023-02-07 12:00:51.166140','2023-02-07 12:00:51.166142','25. KemiKontrol',25),(26,1,'created','2023-02-07 12:00:51.384919','2023-02-07 12:00:51.384921','00. Aflæsninger, målinger, forbrug og fækale uheld',26),(27,1,'created','2023-02-07 12:00:51.734231','2023-02-07 12:00:51.734233','26. Kornlager',27),(28,1,'created','2023-02-07 12:00:52.155880','2023-02-07 12:00:52.155882','00. Logbøger',28);
/*!40000 ALTER TABLE `PlanningTagVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningTags`
--

DROP TABLE IF EXISTS `PlanningTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` varchar(250) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=29 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningTags`
--

LOCK TABLES `PlanningTags` WRITE;
/*!40000 ALTER TABLE `PlanningTags` DISABLE KEYS */;
INSERT INTO `PlanningTags` VALUES (1,'2023-02-07 12:00:44.019218','2023-02-07 12:00:44.019219','created',0,0,1,'01. Logbøger Miljøledelse'),(2,'2023-02-07 12:00:45.514535','2023-02-07 12:00:45.514537','created',0,0,1,'02. Beredskab'),(3,'2023-02-07 12:00:45.806359','2023-02-07 12:00:45.806361','created',0,0,1,'03. Gyllebeholdere'),(4,'2023-02-07 12:00:46.093269','2023-02-07 12:00:46.093270','created',0,0,1,'04. Foderindlægssedler'),(5,'2023-02-07 12:00:46.211354','2023-02-07 12:00:46.211356','created',0,0,1,'05. Stalde: Halebid og klargøring'),(6,'2023-02-07 12:00:46.328945','2023-02-07 12:00:46.328947','created',0,0,1,'06. Fodersiloer'),(7,'2023-02-07 12:00:46.426821','2023-02-07 12:00:46.426823','created',0,0,1,'07. Skadedyrsbekæmpelse'),(8,'2023-02-07 12:00:46.549098','2023-02-07 12:00:46.549100','created',0,0,1,'08. Luftrensning'),(9,'2023-02-07 12:00:46.764716','2023-02-07 12:00:46.764717','created',0,0,1,'09. Forsuring'),(10,'2023-02-07 12:00:46.971529','2023-02-07 12:00:46.971531','created',0,0,1,'10. Varmepumper'),(11,'2023-02-07 12:00:47.230176','2023-02-07 12:00:47.230178','created',0,0,1,'11. Varmekilder'),(12,'2023-02-07 12:00:47.490283','2023-02-07 12:00:47.490285','created',0,0,1,'12. Miljøfarlige stoffer'),(13,'2023-02-07 12:00:47.798598','2023-02-07 12:00:47.798600','created',0,0,1,'13. APV Landbrug'),(14,'2023-02-07 12:00:48.127834','2023-02-07 12:00:48.127836','created',0,0,1,'14. Maskiner'),(15,'2023-02-07 12:00:48.533737','2023-02-07 12:00:48.533739','created',0,0,1,'15. Elværktøj'),(16,'2023-02-07 12:00:48.831948','2023-02-07 12:00:48.831951','created',0,0,1,'16. Stiger'),(17,'2023-02-07 12:00:49.094203','2023-02-07 12:00:49.094205','created',0,0,1,'17. Brandslukkere'),(18,'2023-02-07 12:00:49.402768','2023-02-07 12:00:49.402770','created',0,0,1,'18. Alarm'),(19,'2023-02-07 12:00:49.605971','2023-02-07 12:00:49.605973','created',0,0,1,'19. Ventilation'),(20,'2023-02-07 12:00:49.871033','2023-02-07 12:00:49.871034','created',0,0,1,'20. Ugentlige rutineopgaver'),(21,'2023-02-07 12:00:50.095846','2023-02-07 12:00:50.095850','created',0,0,1,'21. DANISH Standard'),(22,'2023-02-07 12:00:50.336715','2023-02-07 12:00:50.336717','created',0,0,1,'22. Sigtetest'),(23,'2023-02-07 12:00:50.572350','2023-02-07 12:00:50.572352','created',0,0,1,'99. Diverse'),(24,'2023-02-07 12:00:50.871320','2023-02-07 12:00:50.871322','created',0,0,1,'24. IE-indberetning'),(25,'2023-02-07 12:00:51.166140','2023-02-07 12:00:51.166142','created',0,0,1,'25. KemiKontrol'),(26,'2023-02-07 12:00:51.384919','2023-02-07 12:00:51.384921','created',0,0,1,'00. Aflæsninger, målinger, forbrug og fækale uheld'),(27,'2023-02-07 12:00:51.734231','2023-02-07 12:00:51.734233','created',0,0,1,'26. Kornlager'),(28,'2023-02-07 12:00:52.155880','2023-02-07 12:00:52.155882','created',0,0,1,'00. Logbøger');
/*!40000 ALTER TABLE `PlanningTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningVersions`
--

DROP TABLE IF EXISTS `PlanningVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Description` longtext DEFAULT NULL,
  `RepeatEvery` int(11) NOT NULL,
  `RepeatType` int(11) NOT NULL,
  `RepeatUntil` datetime(6) DEFAULT NULL,
  `DayOfWeek` int(11) DEFAULT NULL,
  `DayOfMonth` int(11) DEFAULT NULL,
  `LastExecutedTime` datetime(6) DEFAULT NULL,
  `Enabled` tinyint(1) NOT NULL,
  `RelatedEFormId` int(11) NOT NULL,
  `RelatedEFormName` longtext DEFAULT NULL,
  `PlanningId` int(11) NOT NULL,
  `DeployedAtEnabled` tinyint(1) NOT NULL,
  `DoneAtEnabled` tinyint(1) NOT NULL,
  `DoneByUserNameEnabled` tinyint(1) NOT NULL,
  `UploadedDataEnabled` tinyint(1) NOT NULL,
  `LabelEnabled` tinyint(1) NOT NULL,
  `DescriptionEnabled` tinyint(1) NOT NULL,
  `PlanningNumberEnabled` tinyint(1) NOT NULL,
  `LocationCodeEnabled` tinyint(1) NOT NULL,
  `BuildYearEnabled` tinyint(1) NOT NULL,
  `TypeEnabled` tinyint(1) NOT NULL,
  `NumberOfImagesEnabled` tinyint(1) NOT NULL,
  `SdkFolderName` longtext DEFAULT NULL,
  `SdkParentFolderName` longtext DEFAULT NULL,
  `StartDate` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `BuildYear` longtext DEFAULT NULL,
  `LocationCode` longtext DEFAULT NULL,
  `PlanningNumber` longtext DEFAULT NULL,
  `Type` longtext DEFAULT NULL,
  `SdkFolderId` int(11) DEFAULT NULL,
  `DoneInPeriod` tinyint(1) NOT NULL DEFAULT 0,
  `NextExecutionTime` datetime(6) DEFAULT NULL,
  `PushMessageSent` tinyint(1) NOT NULL DEFAULT 0,
  `DaysBeforeRedeploymentPushMessage` int(11) NOT NULL DEFAULT 5,
  `DaysBeforeRedeploymentPushMessageRepeat` tinyint(1) NOT NULL DEFAULT 0,
  `PushMessageOnDeployment` tinyint(1) NOT NULL DEFAULT 0,
  `IsEditable` tinyint(1) NOT NULL DEFAULT 1,
  `IsHidden` tinyint(1) NOT NULL DEFAULT 0,
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0,
  `ExpireInYears` int(11) NOT NULL DEFAULT 0,
  `ShowExpireDate` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningVersions`
--

LOCK TABLES `PlanningVersions` WRITE;
/*!40000 ALTER TABLE `PlanningVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Plannings`
--

DROP TABLE IF EXISTS `Plannings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Plannings` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Description` longtext DEFAULT NULL,
  `RepeatEvery` int(11) NOT NULL,
  `RepeatType` int(11) NOT NULL,
  `RepeatUntil` datetime(6) DEFAULT NULL,
  `DayOfWeek` int(11) DEFAULT NULL,
  `DayOfMonth` int(11) DEFAULT NULL,
  `LastExecutedTime` datetime(6) DEFAULT NULL,
  `Enabled` tinyint(1) NOT NULL,
  `RelatedEFormId` int(11) NOT NULL,
  `RelatedEFormName` longtext DEFAULT NULL,
  `DeployedAtEnabled` tinyint(1) NOT NULL,
  `DoneAtEnabled` tinyint(1) NOT NULL,
  `DoneByUserNameEnabled` tinyint(1) NOT NULL,
  `UploadedDataEnabled` tinyint(1) NOT NULL,
  `LabelEnabled` tinyint(1) NOT NULL,
  `DescriptionEnabled` tinyint(1) NOT NULL,
  `PlanningNumberEnabled` tinyint(1) NOT NULL,
  `LocationCodeEnabled` tinyint(1) NOT NULL,
  `BuildYearEnabled` tinyint(1) NOT NULL,
  `NumberOfImagesEnabled` tinyint(1) NOT NULL,
  `TypeEnabled` tinyint(1) NOT NULL,
  `SdkFolderName` longtext DEFAULT NULL,
  `SdkParentFolderName` longtext DEFAULT NULL,
  `StartDate` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `BuildYear` longtext DEFAULT NULL,
  `LocationCode` longtext DEFAULT NULL,
  `PlanningNumber` longtext DEFAULT NULL,
  `Type` longtext DEFAULT NULL,
  `SdkFolderId` int(11) DEFAULT NULL,
  `DoneInPeriod` tinyint(1) NOT NULL DEFAULT 0,
  `NextExecutionTime` datetime(6) DEFAULT NULL,
  `PushMessageSent` tinyint(1) NOT NULL DEFAULT 0,
  `DaysBeforeRedeploymentPushMessage` int(11) NOT NULL DEFAULT 5,
  `DaysBeforeRedeploymentPushMessageRepeat` tinyint(1) NOT NULL DEFAULT 0,
  `PushMessageOnDeployment` tinyint(1) NOT NULL DEFAULT 0,
  `IsEditable` tinyint(1) NOT NULL DEFAULT 1,
  `IsHidden` tinyint(1) NOT NULL DEFAULT 0,
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0,
  `ExpireInYears` int(11) NOT NULL DEFAULT 0,
  `ShowExpireDate` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Plannings`
--

LOCK TABLES `Plannings` WRITE;
/*!40000 ALTER TABLE `Plannings` DISABLE KEYS */;
/*!40000 ALTER TABLE `Plannings` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningsTags`
--

DROP TABLE IF EXISTS `PlanningsTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningsTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PlanningTagId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanningsTags_PlanningId` (`PlanningId`),
  KEY `IX_PlanningsTags_PlanningTagId` (`PlanningTagId`),
  CONSTRAINT `FK_PlanningsTags_PlanningTags_PlanningTagId` FOREIGN KEY (`PlanningTagId`) REFERENCES `PlanningTags` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_PlanningsTags_Plannings_PlanningId` FOREIGN KEY (`PlanningId`) REFERENCES `Plannings` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningsTags`
--

LOCK TABLES `PlanningsTags` WRITE;
/*!40000 ALTER TABLE `PlanningsTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningsTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanningsTagsVersions`
--

DROP TABLE IF EXISTS `PlanningsTagsVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanningsTagsVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `PlanningTagId` int(11) NOT NULL,
  `PlanningId` int(11) NOT NULL,
  `PlanningsTagsId` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanningsTagsVersions`
--

LOCK TABLES `PlanningsTagsVersions` WRITE;
/*!40000 ALTER TABLE `PlanningsTagsVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanningsTagsVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PluginConfigurationValueVersions`
--

DROP TABLE IF EXISTS `PluginConfigurationValueVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PluginConfigurationValueVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Value` longtext DEFAULT NULL,
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
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Value` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=6 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginConfigurationValues`
--

LOCK TABLES `PluginConfigurationValues` WRITE;
/*!40000 ALTER TABLE `PluginConfigurationValues` DISABLE KEYS */;
INSERT INTO `PluginConfigurationValues` VALUES (1,'2023-02-07 11:45:32.530411','2023-02-07 11:45:32.530750','created',1,0,1,'ItemsPlanningBaseSettings:ReportSubHeaderName',''),(2,'2023-02-07 11:45:32.559755','2023-02-07 11:45:32.559756','created',1,0,1,'ItemsPlanningBaseSettings:ReportHeaderName',''),(3,'2023-02-07 11:45:32.564149','2023-02-07 11:45:32.564151','created',1,0,1,'ItemsPlanningBaseSettings:StartTime','7'),(4,'2023-02-07 11:45:32.569044','2023-02-07 11:45:32.569047','created',1,0,1,'ItemsPlanningBaseSettings:EndTime','9'),(5,'2023-02-07 11:45:32.573420','2023-02-07 11:45:32.573421','created',1,0,1,'ItemsPlanningBaseSettings:ReportImageName','');
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
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `GroupId` int(11) NOT NULL,
  `PermissionId` int(11) NOT NULL,
  `IsEnabled` tinyint(1) NOT NULL,
  `PluginGroupPermissionId` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissionVersions`
--

LOCK TABLES `PluginGroupPermissionVersions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissionVersions` DISABLE KEYS */;
INSERT INTO `PluginGroupPermissionVersions` VALUES (1,'2023-02-07 11:45:32.878712','2023-02-07 11:45:32.878715','created',0,0,1,1,1,1,1),(2,'2023-02-07 11:45:33.115051','2023-02-07 11:45:33.115053','created',0,0,1,1,2,1,2),(3,'2023-02-07 11:45:33.130364','2023-02-07 11:45:33.130366','created',0,0,1,1,4,1,3),(4,'2023-02-07 11:45:33.145897','2023-02-07 11:45:33.145898','created',0,0,1,1,3,1,4);
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
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `GroupId` int(11) NOT NULL,
  `PermissionId` int(11) NOT NULL,
  `IsEnabled` tinyint(1) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PluginGroupPermissions_PermissionId` (`PermissionId`),
  CONSTRAINT `FK_PluginGroupPermissions_PluginPermissions_PermissionId` FOREIGN KEY (`PermissionId`) REFERENCES `PluginPermissions` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissions`
--

LOCK TABLES `PluginGroupPermissions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissions` DISABLE KEYS */;
INSERT INTO `PluginGroupPermissions` VALUES (1,'2023-02-07 11:45:32.878712','2023-02-07 11:45:32.878715','created',0,0,1,1,1,1),(2,'2023-02-07 11:45:33.115051','2023-02-07 11:45:33.115053','created',0,0,1,1,2,1),(3,'2023-02-07 11:45:33.130364','2023-02-07 11:45:33.130366','created',0,0,1,1,4,1),(4,'2023-02-07 11:45:33.145897','2023-02-07 11:45:33.145898','created',0,0,1,1,3,1);
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
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PermissionName` longtext DEFAULT NULL,
  `ClaimName` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginPermissions`
--

LOCK TABLES `PluginPermissions` WRITE;
/*!40000 ALTER TABLE `PluginPermissions` DISABLE KEYS */;
INSERT INTO `PluginPermissions` VALUES (1,'2023-02-07 11:45:32.591402',NULL,'created',1,0,1,'Access ItemsPlanning Plugin','items_planning_plugin_access'),(2,'2023-02-07 11:45:32.610737',NULL,'created',1,0,1,'Create Notification Rules','plannings_create'),(3,'2023-02-07 11:45:32.612018',NULL,'created',1,0,1,'Edit Planning','planning_edit'),(4,'2023-02-07 11:45:32.613313',NULL,'created',1,0,1,'Obtain plannings','plannings_get');
/*!40000 ALTER TABLE `PluginPermissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UploadedDataVersions`
--

DROP TABLE IF EXISTS `UploadedDataVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UploadedDataVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PlanningCaseId` int(11) NOT NULL,
  `Checksum` varchar(255) DEFAULT NULL,
  `Extension` varchar(255) DEFAULT NULL,
  `CurrentFile` varchar(255) DEFAULT NULL,
  `UploaderType` varchar(255) DEFAULT NULL,
  `FileLocation` varchar(255) DEFAULT NULL,
  `FileName` varchar(255) DEFAULT NULL,
  `UploadedDataId` int(11) NOT NULL,
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
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PlanningCaseId` int(11) NOT NULL,
  `Checksum` varchar(255) DEFAULT NULL,
  `Extension` varchar(255) DEFAULT NULL,
  `CurrentFile` varchar(255) DEFAULT NULL,
  `UploaderType` varchar(255) DEFAULT NULL,
  `FileLocation` varchar(255) DEFAULT NULL,
  `FileName` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`)
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
INSERT INTO `__EFMigrationsHistory` VALUES ('20200713164327_Init','7.0.2'),('20200717124153_AddPlanningSites','7.0.2'),('20200804131937_FixingMigrations','7.0.2'),('20200810120121_AddingFolderIdToItem','7.0.2'),('20200812142006_AddingFolderNameToPlanning','7.0.2'),('20200928113840_Add PlanningTag, PlanningsTags and versions for them','7.0.2'),('20200929105530_Add SdkParentFolderName to Planning And PlanningVersion','7.0.2'),('20201112141501_AddPlanningStartDate','7.0.2'),('20201112162947_AddPlanningSiteExecutedTime','7.0.2'),('20210106140038_AddTranslateForPlaningName','7.0.2'),('20210217120024_MergeItemAndPlanning','7.0.2'),('20210306105112_AddinSdkFolderId','7.0.2'),('20210306111003_SettingFolderIdToNullable','7.0.2'),('20210316043301_AddingAttributesForPush','7.0.2'),('20210316124948_AddingPushMessageSent','7.0.2'),('20210520093038_AddDaysBeforeRedeploymentPushMessage','7.0.2'),('20210709081456_AddingPushMessageOnDeployment','7.0.2'),('20211024130122_AddingLockHiddenEditableAttributes','7.0.2'),('20211118163546_AddingMicrotingCheckListSitId','7.0.2'),('20221130154310_RemoveLanguageFK','7.0.2'),('20221130154616_RemoveLanguageFK1','7.0.2'),('20221201032235_AddingBackLanguageFK','7.0.2');
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

-- Dump completed on 2023-02-08 16:05:34
