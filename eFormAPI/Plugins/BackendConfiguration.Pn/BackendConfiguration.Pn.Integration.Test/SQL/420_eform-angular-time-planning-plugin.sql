-- MariaDB dump 10.19  Distrib 10.6.11-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: localhost    Database: 420_eform-angular-time-planning-plugin
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
-- Table structure for table `AssignedSiteVersions`
--

DROP TABLE IF EXISTS `AssignedSiteVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AssignedSiteVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `SiteId` int(11) NOT NULL,
  `AssignedSiteId` int(11) NOT NULL,
  `CaseMicrotingUid` int(11) DEFAULT NULL,
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
-- Dumping data for table `AssignedSiteVersions`
--

LOCK TABLES `AssignedSiteVersions` WRITE;
/*!40000 ALTER TABLE `AssignedSiteVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AssignedSiteVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AssignedSites`
--

DROP TABLE IF EXISTS `AssignedSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AssignedSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `SiteId` int(11) NOT NULL,
  `CaseMicrotingUid` int(11) DEFAULT NULL,
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
-- Dumping data for table `AssignedSites`
--

LOCK TABLES `AssignedSites` WRITE;
/*!40000 ALTER TABLE `AssignedSites` DISABLE KEYS */;
/*!40000 ALTER TABLE `AssignedSites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Messages`
--

DROP TABLE IF EXISTS `Messages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Messages` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` longtext DEFAULT NULL,
  `DaName` longtext DEFAULT NULL,
  `DeName` longtext DEFAULT NULL,
  `EnName` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=9 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Messages`
--

LOCK TABLES `Messages` WRITE;
/*!40000 ALTER TABLE `Messages` DISABLE KEYS */;
INSERT INTO `Messages` VALUES (1,'DayOff','Fridag','Freier Tag','Day off'),(2,'Vacation','Ferie','Urlaub','Vacation'),(3,'Sick','Syg','Krank','Sick'),(4,'Course','Kursus','Kurs','Course'),(5,'LeaveOfAbsence','Orlov','Urlaub','Leave of absence'),(7,'Children1stSick','Barn 1. sygedag','1. Krankheitstag der Kinder','Children 1st sick'),(8,'Children2ndSick','Barn 2. sygedag','2. Krankheitstag der Kinder','Children 2nd sick');
/*!40000 ALTER TABLE `Messages` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanRegistrationVersions`
--

DROP TABLE IF EXISTS `PlanRegistrationVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanRegistrationVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `SdkSitId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `PlanText` longtext DEFAULT NULL,
  `PlanHours` double NOT NULL,
  `Start1Id` int(11) NOT NULL,
  `Stop1Id` int(11) NOT NULL,
  `Pause1Id` int(11) NOT NULL,
  `Start2Id` int(11) NOT NULL,
  `Stop2Id` int(11) NOT NULL,
  `Pause2Id` int(11) NOT NULL,
  `NettoHours` double NOT NULL,
  `Flex` double NOT NULL,
  `SumFlexEnd` double NOT NULL,
  `PaiedOutFlex` double NOT NULL,
  `MessageId` int(11) DEFAULT NULL,
  `CommentOffice` longtext DEFAULT NULL,
  `CommentOfficeAll` longtext DEFAULT NULL,
  `PlanRegistrationId` int(11) NOT NULL,
  `StatusCaseId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `WorkerComment` longtext DEFAULT NULL,
  `SumFlexStart` double NOT NULL DEFAULT 0,
  `DataFromDevice` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanRegistrationVersions`
--

LOCK TABLES `PlanRegistrationVersions` WRITE;
/*!40000 ALTER TABLE `PlanRegistrationVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanRegistrationVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PlanRegistrations`
--

DROP TABLE IF EXISTS `PlanRegistrations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PlanRegistrations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `SdkSitId` int(11) NOT NULL,
  `Date` datetime(6) NOT NULL,
  `PlanText` longtext DEFAULT NULL,
  `PlanHours` double NOT NULL,
  `Start1Id` int(11) NOT NULL,
  `Stop1Id` int(11) NOT NULL,
  `Pause1Id` int(11) NOT NULL,
  `Start2Id` int(11) NOT NULL,
  `Stop2Id` int(11) NOT NULL,
  `Pause2Id` int(11) NOT NULL,
  `NettoHours` double NOT NULL,
  `Flex` double NOT NULL,
  `SumFlexEnd` double NOT NULL,
  `PaiedOutFlex` double NOT NULL,
  `MessageId` int(11) DEFAULT NULL,
  `CommentOffice` longtext DEFAULT NULL,
  `CommentOfficeAll` longtext DEFAULT NULL,
  `StatusCaseId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `WorkerComment` longtext DEFAULT NULL,
  `SumFlexStart` double NOT NULL DEFAULT 0,
  `DataFromDevice` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanRegistrations_MessageId` (`MessageId`),
  CONSTRAINT `FK_PlanRegistrations_Messages_MessageId` FOREIGN KEY (`MessageId`) REFERENCES `Messages` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PlanRegistrations`
--

LOCK TABLES `PlanRegistrations` WRITE;
/*!40000 ALTER TABLE `PlanRegistrations` DISABLE KEYS */;
/*!40000 ALTER TABLE `PlanRegistrations` ENABLE KEYS */;
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
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginConfigurationValueVersions`
--

LOCK TABLES `PluginConfigurationValueVersions` WRITE;
/*!40000 ALTER TABLE `PluginConfigurationValueVersions` DISABLE KEYS */;
INSERT INTO `PluginConfigurationValueVersions` VALUES (1,'TimePlanningBaseSettings:EformId','0','2023-02-07 11:52:14.157261','2023-02-07 11:52:14.157264','created',0,0,1),(2,'TimePlanningBaseSettings:InfoeFormId','0','2023-02-07 11:52:23.028429','2023-02-07 11:52:23.028431','created',0,0,1),(3,'TimePlanningBaseSettings:FolderId','0','2023-02-07 11:52:34.682869','2023-02-07 11:52:34.682871','created',0,0,1);
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
) ENGINE=InnoDB AUTO_INCREMENT=6 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginConfigurationValues`
--

LOCK TABLES `PluginConfigurationValues` WRITE;
/*!40000 ALTER TABLE `PluginConfigurationValues` DISABLE KEYS */;
INSERT INTO `PluginConfigurationValues` VALUES (1,'TimePlanningBaseSettings:FolderId','1','2023-02-07 11:46:51.670696','2023-02-07 11:52:34.683121','created',1,0,2),(2,'TimePlanningBaseSettings:EformId','1','2023-02-07 11:46:51.707932','2023-02-07 11:52:14.853272','created',1,0,2),(3,'TimePlanningBaseSettings:InfoeFormId','4','2023-02-07 11:46:51.712726','2023-02-07 11:52:23.029025','created',1,0,2),(4,'TimePlanningBaseSettings:MaxHistoryDays','30','2023-02-07 11:46:51.717007','2023-02-07 11:46:51.717008','created',1,0,1),(5,'TimePlanningBaseSettings:MaxDaysEditable','45','2023-02-07 11:46:51.720670','2023-02-07 11:46:51.720671','created',1,0,1);
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
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissionVersions`
--

LOCK TABLES `PluginGroupPermissionVersions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissionVersions` DISABLE KEYS */;
INSERT INTO `PluginGroupPermissionVersions` VALUES (1,1,2,1,1,'2023-02-07 11:46:51.911727','2023-02-07 11:46:51.911729','created',0,0,1),(2,1,1,1,2,'2023-02-07 11:46:52.589916','2023-02-07 11:46:52.589918','created',0,0,1),(3,1,3,1,3,'2023-02-07 11:46:52.837743','2023-02-07 11:46:52.837745','created',0,0,1);
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
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissions`
--

LOCK TABLES `PluginGroupPermissions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissions` DISABLE KEYS */;
INSERT INTO `PluginGroupPermissions` VALUES (1,1,2,1,'2023-02-07 11:46:51.911727','2023-02-07 11:46:51.911729','created',0,0,1),(2,1,1,1,'2023-02-07 11:46:52.589916','2023-02-07 11:46:52.589918','created',0,0,1),(3,1,3,1,'2023-02-07 11:46:52.837743','2023-02-07 11:46:52.837745','created',0,0,1);
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
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginPermissions`
--

LOCK TABLES `PluginPermissions` WRITE;
/*!40000 ALTER TABLE `PluginPermissions` DISABLE KEYS */;
INSERT INTO `PluginPermissions` VALUES (1,'Access Time Plannings Plugin','time_planning_plugin_access','2023-02-07 11:46:51.728782',NULL,'created',1,0,1),(2,'Obtain flex','time_planning_flex_get','2023-02-07 11:46:51.748121',NULL,'created',1,0,1),(3,'Obtain working hours','time_planning_working_hours_get','2023-02-07 11:46:51.749560',NULL,'created',1,0,1);
/*!40000 ALTER TABLE `PluginPermissions` ENABLE KEYS */;
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
INSERT INTO `__EFMigrationsHistory` VALUES ('20211202224031_InitialCreate','7.0.2'),('20211203051857_AddingWorkerComment','7.0.2'),('20211209152624_AddingTranslationsToMessages','7.0.2'),('20220511073516_AddingSumFlexStartEnd','7.0.2'),('20220705191333_AddingDataFromDeviceToPlanRegistration','7.0.2');
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

-- Dump completed on 2023-02-08 16:05:51
