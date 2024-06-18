-- MariaDB dump 10.19  Distrib 10.6.16-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: 127.0.0.1    Database: 420_eform-angular-time-planning-plugin
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
  `RegistrationDeviceId` int(11) DEFAULT NULL,
  `Pause1StartedAt` datetime(6) DEFAULT NULL,
  `Pause1StoppedAt` datetime(6) DEFAULT NULL,
  `Pause2StartedAt` datetime(6) DEFAULT NULL,
  `Pause2StoppedAt` datetime(6) DEFAULT NULL,
  `Start1StartedAt` datetime(6) DEFAULT NULL,
  `Start2StartedAt` datetime(6) DEFAULT NULL,
  `Stop1StoppedAt` datetime(6) DEFAULT NULL,
  `Stop2StoppedAt` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  `RegistrationDeviceId` int(11) DEFAULT NULL,
  `Pause1StartedAt` datetime(6) DEFAULT NULL,
  `Pause1StoppedAt` datetime(6) DEFAULT NULL,
  `Pause2StartedAt` datetime(6) DEFAULT NULL,
  `Pause2StoppedAt` datetime(6) DEFAULT NULL,
  `Start1StartedAt` datetime(6) DEFAULT NULL,
  `Start2StartedAt` datetime(6) DEFAULT NULL,
  `Stop1StoppedAt` datetime(6) DEFAULT NULL,
  `Stop2StoppedAt` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_PlanRegistrations_MessageId` (`MessageId`),
  CONSTRAINT `FK_PlanRegistrations_Messages_MessageId` FOREIGN KEY (`MessageId`) REFERENCES `Messages` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
) ENGINE=InnoDB AUTO_INCREMENT=7 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
-- Table structure for table `RegistrationDeviceVersions`
--

DROP TABLE IF EXISTS `RegistrationDeviceVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `RegistrationDeviceVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Token` longtext DEFAULT NULL,
  `SoftwareVersion` longtext DEFAULT NULL,
  `Model` longtext DEFAULT NULL,
  `Manufacturer` longtext DEFAULT NULL,
  `OsVersion` longtext DEFAULT NULL,
  `LastIp` longtext DEFAULT NULL,
  `LastKnownLocation` longtext DEFAULT NULL,
  `LookedUpIp` longtext DEFAULT NULL,
  `OtpCode` longtext DEFAULT NULL,
  `OtpEnabled` tinyint(1) NOT NULL,
  `RegistrationDeviceId` int(11) NOT NULL,
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
-- Table structure for table `RegistrationDevices`
--

DROP TABLE IF EXISTS `RegistrationDevices`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `RegistrationDevices` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Token` longtext DEFAULT NULL,
  `SoftwareVersion` longtext DEFAULT NULL,
  `Model` longtext DEFAULT NULL,
  `Manufacturer` longtext DEFAULT NULL,
  `OsVersion` longtext DEFAULT NULL,
  `LastIp` longtext DEFAULT NULL,
  `LastKnownLocation` longtext DEFAULT NULL,
  `LookedUpIp` longtext DEFAULT NULL,
  `OtpCode` longtext DEFAULT NULL,
  `OtpEnabled` tinyint(1) NOT NULL,
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
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2024-06-13 11:58:03
