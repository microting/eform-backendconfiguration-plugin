-- MariaDB dump 10.19  Distrib 10.6.16-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: 127.0.0.1    Database: 420_eform-angular-items-planning-plugin
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
  CONSTRAINT `FK_PlanningNameTranslation_Plannings_PlanningId` FOREIGN KEY (`PlanningId`) REFERENCES `Plannings` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  `ReportGroupPlanningTagId` int(11) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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
  `ReportGroupPlanningTagId` int(11) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

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

-- Dump completed on 2024-06-13 11:57:44
