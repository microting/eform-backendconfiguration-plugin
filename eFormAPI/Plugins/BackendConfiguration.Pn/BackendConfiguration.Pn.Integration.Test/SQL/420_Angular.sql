-- MariaDB dump 10.19  Distrib 10.6.11-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: localhost    Database: 420_Angular
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
-- Table structure for table `CasePostEmailRecipients`
--

DROP TABLE IF EXISTS `CasePostEmailRecipients`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `CasePostEmailRecipients` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EmailRecipientId` int(11) NOT NULL,
  `CasePostId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_CasePostEmailRecipients_CasePostId` (`CasePostId`),
  KEY `IX_CasePostEmailRecipients_EmailRecipientId` (`EmailRecipientId`),
  CONSTRAINT `FK_CasePostEmailRecipients_CasePosts_CasePostId` FOREIGN KEY (`CasePostId`) REFERENCES `CasePosts` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_CasePostEmailRecipients_EmailRecipients_EmailRecipientId` FOREIGN KEY (`EmailRecipientId`) REFERENCES `EmailRecipients` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `CasePostEmailRecipients`
--

LOCK TABLES `CasePostEmailRecipients` WRITE;
/*!40000 ALTER TABLE `CasePostEmailRecipients` DISABLE KEYS */;
/*!40000 ALTER TABLE `CasePostEmailRecipients` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `CasePostEmailTags`
--

DROP TABLE IF EXISTS `CasePostEmailTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `CasePostEmailTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EmailTagId` int(11) NOT NULL,
  `CasePostId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_CasePostEmailTags_CasePostId` (`CasePostId`),
  KEY `IX_CasePostEmailTags_EmailTagId` (`EmailTagId`),
  CONSTRAINT `FK_CasePostEmailTags_CasePosts_CasePostId` FOREIGN KEY (`CasePostId`) REFERENCES `CasePosts` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_CasePostEmailTags_EmailTags_EmailTagId` FOREIGN KEY (`EmailTagId`) REFERENCES `EmailTags` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `CasePostEmailTags`
--

LOCK TABLES `CasePostEmailTags` WRITE;
/*!40000 ALTER TABLE `CasePostEmailTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `CasePostEmailTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `CasePosts`
--

DROP TABLE IF EXISTS `CasePosts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `CasePosts` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PostDate` datetime(6) NOT NULL,
  `Subject` longtext NOT NULL,
  `Text` longtext NOT NULL,
  `LinkToCase` tinyint(1) NOT NULL,
  `AttachPdf` tinyint(1) NOT NULL,
  `CaseId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_CasePosts_CaseId` (`CaseId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `CasePosts`
--

LOCK TABLES `CasePosts` WRITE;
/*!40000 ALTER TABLE `CasePosts` DISABLE KEYS */;
/*!40000 ALTER TABLE `CasePosts` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ConfigurationValues`
--

DROP TABLE IF EXISTS `ConfigurationValues`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ConfigurationValues` (
  `Id` varchar(255) NOT NULL,
  `Value` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_ConfigurationValues_Id` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ConfigurationValues`
--

LOCK TABLES `ConfigurationValues` WRITE;
/*!40000 ALTER TABLE `ConfigurationValues` DISABLE KEYS */;
INSERT INTO `ConfigurationValues` VALUES ('ApplicationSettings:DefaultLocale','da'),('ApplicationSettings:DefaultPassword','Qq1234567$'),('ApplicationSettings:IsTwoFactorForced','False'),('ApplicationSettings:IsUserbackWidgetEnabled','False'),('ApplicationSettings:SecurityCode','code'),('ApplicationSettings:SiteLink',''),('ApplicationSettings:UserbackToken','33542|62605|dEaGb7GN0RoGEOMwEEWGh1pnh'),('ConnectionStringsSdk:SdkConnection','host= localhost;Database=420_SDK;user = root; password = secretpassword;port=3306;Convert Zero Datetime = true;SslMode=none;'),('EformTokenOptions:Audience','eForm Angular'),('EformTokenOptions:CookieName','Authorization'),('EformTokenOptions:Expiration','12:00:00'),('EformTokenOptions:Issuer','eForm API'),('EformTokenOptions:SigningKey','r4O4gJ5p97F0k57S7QDm0Y4W6KFuCHzlT6pWwiBtiyM='),('EmailSettings:Login',''),('EmailSettings:Password',''),('EmailSettings:SendGridKey',''),('EmailSettings:SmtpHost',''),('EmailSettings:SmtpPort','25'),('HeaderSettings:ImageLink',''),('HeaderSettings:ImageLinkVisible','true'),('HeaderSettings:MainText','eForm Backend'),('HeaderSettings:MainTextVisible','true'),('HeaderSettings:SecondaryText','No more paper-forms and back-office data entry'),('HeaderSettings:SecondaryTextVisible','true'),('Logging:IncludeScopes','false'),('Logging:IncludeScopes:LogLevel:Default','Debug'),('Logging:IncludeScopes:LogLevel:Microsoft','Information'),('Logging:IncludeScopes:LogLevel:System','Information'),('LoginPageSettings:ImageLink',''),('LoginPageSettings:ImageLinkVisible','true'),('LoginPageSettings:MainText','eForm Backend'),('LoginPageSettings:MainTextVisible','true'),('LoginPageSettings:SecondaryText','No more paper-forms and back-office data entry'),('LoginPageSettings:SecondaryTextVisible','true'),('PluginStoreSettings:PluginListLink','https://raw.githubusercontent.com/microting/eform-angular-frontend/stable/plugins.json');
/*!40000 ALTER TABLE `ConfigurationValues` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EformInGroups`
--

DROP TABLE IF EXISTS `EformInGroups`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EformInGroups` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `TemplateId` int(11) NOT NULL,
  `SecurityGroupId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EformInGroups_TemplateId_SecurityGroupId` (`TemplateId`,`SecurityGroupId`),
  KEY `IX_EformInGroups_SecurityGroupId` (`SecurityGroupId`),
  KEY `IX_EformInGroups_TemplateId` (`TemplateId`),
  CONSTRAINT `FK_EformInGroups_SecurityGroups_SecurityGroupId` FOREIGN KEY (`SecurityGroupId`) REFERENCES `SecurityGroups` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EformInGroups`
--

LOCK TABLES `EformInGroups` WRITE;
/*!40000 ALTER TABLE `EformInGroups` DISABLE KEYS */;
/*!40000 ALTER TABLE `EformInGroups` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EformPermissions`
--

DROP TABLE IF EXISTS `EformPermissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EformPermissions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PermissionId` int(11) NOT NULL,
  `EformInGroupId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EformPermissions_PermissionId_EformInGroupId` (`PermissionId`,`EformInGroupId`),
  KEY `IX_EformPermissions_EformInGroupId` (`EformInGroupId`),
  CONSTRAINT `FK_EformPermissions_EformInGroups_EformInGroupId` FOREIGN KEY (`EformInGroupId`) REFERENCES `EformInGroups` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_EformPermissions_Permissions_PermissionId` FOREIGN KEY (`PermissionId`) REFERENCES `Permissions` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EformPermissions`
--

LOCK TABLES `EformPermissions` WRITE;
/*!40000 ALTER TABLE `EformPermissions` DISABLE KEYS */;
/*!40000 ALTER TABLE `EformPermissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EformPlugins`
--

DROP TABLE IF EXISTS `EformPlugins`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EformPlugins` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PluginId` varchar(100) NOT NULL,
  `ConnectionString` longtext DEFAULT NULL,
  `Status` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EformPlugins_PluginId` (`PluginId`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EformPlugins`
--

LOCK TABLES `EformPlugins` WRITE;
/*!40000 ALTER TABLE `EformPlugins` DISABLE KEYS */;
INSERT INTO `EformPlugins` VALUES (1,'eform-angular-time-planning-plugin','host= localhost;Database=420_eform-angular-time-planning-plugin;user = root; password = secretpassword;port=3306;Convert Zero Datetime = true;SslMode=none;PersistSecurityInfo=true;',2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(2,'eform-backend-configuration-plugin','host= localhost;Database=420_eform-backend-configuration-plugin;user = root; password = secretpassword;port=3306;Convert Zero Datetime = true;SslMode=none;PersistSecurityInfo=true;',2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(3,'eform-angular-items-planning-plugin','host= localhost;Database=420_eform-angular-items-planning-plugin;user = root; password = secretpassword;port=3306;Convert Zero Datetime = true;SslMode=none;PersistSecurityInfo=true;',2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL);
/*!40000 ALTER TABLE `EformPlugins` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EformReportDataItems`
--

DROP TABLE IF EXISTS `EformReportDataItems`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EformReportDataItems` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DataItemId` int(11) NOT NULL,
  `Position` int(11) NOT NULL,
  `Visibility` tinyint(1) NOT NULL,
  `EformReportElementId` int(11) NOT NULL,
  `ParentId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_EformReportDataItems_DataItemId` (`DataItemId`),
  KEY `IX_EformReportDataItems_EformReportElementId` (`EformReportElementId`),
  KEY `IX_EformReportDataItems_ParentId` (`ParentId`),
  CONSTRAINT `FK_EformReportDataItems_EformReportDataItems_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `EformReportDataItems` (`Id`),
  CONSTRAINT `FK_EformReportDataItems_EformReportElements_EformReportElementId` FOREIGN KEY (`EformReportElementId`) REFERENCES `EformReportElements` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EformReportDataItems`
--

LOCK TABLES `EformReportDataItems` WRITE;
/*!40000 ALTER TABLE `EformReportDataItems` DISABLE KEYS */;
/*!40000 ALTER TABLE `EformReportDataItems` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EformReportElements`
--

DROP TABLE IF EXISTS `EformReportElements`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EformReportElements` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ElementId` int(11) NOT NULL,
  `EformReportId` int(11) NOT NULL,
  `ParentId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_EformReportElements_EformReportId` (`EformReportId`),
  KEY `IX_EformReportElements_ElementId` (`ElementId`),
  KEY `IX_EformReportElements_ParentId` (`ParentId`),
  CONSTRAINT `FK_EformReportElements_EformReportElements_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `EformReportElements` (`Id`),
  CONSTRAINT `FK_EformReportElements_EformReports_EformReportId` FOREIGN KEY (`EformReportId`) REFERENCES `EformReports` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EformReportElements`
--

LOCK TABLES `EformReportElements` WRITE;
/*!40000 ALTER TABLE `EformReportElements` DISABLE KEYS */;
/*!40000 ALTER TABLE `EformReportElements` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EformReports`
--

DROP TABLE IF EXISTS `EformReports`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EformReports` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `TemplateId` int(11) NOT NULL,
  `HeaderImage` longblob DEFAULT NULL,
  `HeaderVisibility` longtext DEFAULT NULL,
  `IsDateVisible` tinyint(1) NOT NULL,
  `IsWorkerNameVisible` tinyint(1) NOT NULL,
  `Description` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_EformReports_TemplateId` (`TemplateId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EformReports`
--

LOCK TABLES `EformReports` WRITE;
/*!40000 ALTER TABLE `EformReports` DISABLE KEYS */;
/*!40000 ALTER TABLE `EformReports` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EmailRecipients`
--

DROP TABLE IF EXISTS `EmailRecipients`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EmailRecipients` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` varchar(250) NOT NULL,
  `Email` varchar(250) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_EmailRecipients_Email` (`Email`),
  KEY `IX_EmailRecipients_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EmailRecipients`
--

LOCK TABLES `EmailRecipients` WRITE;
/*!40000 ALTER TABLE `EmailRecipients` DISABLE KEYS */;
/*!40000 ALTER TABLE `EmailRecipients` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EmailTagRecipients`
--

DROP TABLE IF EXISTS `EmailTagRecipients`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EmailTagRecipients` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `EmailTagId` int(11) NOT NULL,
  `EmailRecipientId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_EmailTagRecipients_EmailRecipientId` (`EmailRecipientId`),
  KEY `IX_EmailTagRecipients_EmailTagId` (`EmailTagId`),
  CONSTRAINT `FK_EmailTagRecipients_EmailRecipients_EmailRecipientId` FOREIGN KEY (`EmailRecipientId`) REFERENCES `EmailRecipients` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_EmailTagRecipients_EmailTags_EmailTagId` FOREIGN KEY (`EmailTagId`) REFERENCES `EmailTags` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EmailTagRecipients`
--

LOCK TABLES `EmailTagRecipients` WRITE;
/*!40000 ALTER TABLE `EmailTagRecipients` DISABLE KEYS */;
/*!40000 ALTER TABLE `EmailTagRecipients` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `EmailTags`
--

DROP TABLE IF EXISTS `EmailTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `EmailTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` varchar(250) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_EmailTags_Name` (`Name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `EmailTags`
--

LOCK TABLES `EmailTags` WRITE;
/*!40000 ALTER TABLE `EmailTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `EmailTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `GroupPermissions`
--

DROP TABLE IF EXISTS `GroupPermissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `GroupPermissions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PermissionId` int(11) NOT NULL,
  `SecurityGroupId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_GroupPermissions_PermissionId_SecurityGroupId` (`PermissionId`,`SecurityGroupId`),
  KEY `IX_GroupPermissions_SecurityGroupId` (`SecurityGroupId`),
  CONSTRAINT `FK_GroupPermissions_Permissions_PermissionId` FOREIGN KEY (`PermissionId`) REFERENCES `Permissions` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_GroupPermissions_SecurityGroups_SecurityGroupId` FOREIGN KEY (`SecurityGroupId`) REFERENCES `SecurityGroups` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=31 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `GroupPermissions`
--

LOCK TABLES `GroupPermissions` WRITE;
/*!40000 ALTER TABLE `GroupPermissions` DISABLE KEYS */;
INSERT INTO `GroupPermissions` VALUES (1,29,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(2,27,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(3,28,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(4,30,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(5,31,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(6,32,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(7,34,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(8,33,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(9,35,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(10,36,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(11,42,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(12,37,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(13,38,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(14,39,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(15,41,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(16,40,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(17,29,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(18,42,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(19,34,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(20,33,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(21,35,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(22,37,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(23,43,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(24,44,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(25,45,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(26,46,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(27,47,1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(28,49,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(29,50,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(30,51,2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL);
/*!40000 ALTER TABLE `GroupPermissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `MenuItemSecurityGroups`
--

DROP TABLE IF EXISTS `MenuItemSecurityGroups`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `MenuItemSecurityGroups` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `MenuItemId` int(11) NOT NULL,
  `SecurityGroupId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_MenuItemSecurityGroups_MenuItemId_SecurityGroupId` (`MenuItemId`,`SecurityGroupId`),
  KEY `IX_MenuItemSecurityGroups_SecurityGroupId` (`SecurityGroupId`),
  CONSTRAINT `FK_MenuItemSecurityGroups_MenuItems_MenuItemId` FOREIGN KEY (`MenuItemId`) REFERENCES `MenuItems` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_MenuItemSecurityGroups_SecurityGroups_SecurityGroupId` FOREIGN KEY (`SecurityGroupId`) REFERENCES `SecurityGroups` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=26 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `MenuItemSecurityGroups`
--

LOCK TABLES `MenuItemSecurityGroups` WRITE;
/*!40000 ALTER TABLE `MenuItemSecurityGroups` DISABLE KEYS */;
INSERT INTO `MenuItemSecurityGroups` VALUES (1,NULL,NULL,NULL,NULL,1,1),(2,NULL,NULL,NULL,NULL,2,1),(3,NULL,NULL,NULL,NULL,3,1),(4,NULL,NULL,NULL,NULL,4,1),(5,NULL,NULL,NULL,NULL,5,1),(6,NULL,NULL,NULL,NULL,6,1),(7,NULL,NULL,NULL,NULL,7,1),(8,NULL,NULL,NULL,NULL,8,1),(9,NULL,NULL,NULL,NULL,9,1),(10,NULL,NULL,NULL,NULL,10,1),(11,NULL,NULL,NULL,NULL,11,1),(12,NULL,NULL,NULL,NULL,12,1),(13,NULL,NULL,NULL,NULL,19,1),(14,NULL,NULL,NULL,NULL,20,1),(15,NULL,NULL,NULL,NULL,21,1),(16,NULL,NULL,NULL,NULL,22,1),(17,NULL,NULL,NULL,NULL,23,1),(18,NULL,NULL,NULL,NULL,24,1),(19,NULL,NULL,NULL,NULL,25,1),(20,NULL,NULL,NULL,NULL,26,1),(21,NULL,NULL,NULL,NULL,27,1),(22,NULL,NULL,NULL,NULL,28,1),(23,NULL,NULL,NULL,NULL,29,1),(24,NULL,NULL,NULL,NULL,30,1),(25,NULL,NULL,NULL,NULL,31,1);
/*!40000 ALTER TABLE `MenuItemSecurityGroups` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `MenuItemTranslations`
--

DROP TABLE IF EXISTS `MenuItemTranslations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `MenuItemTranslations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Name` varchar(250) DEFAULT NULL,
  `LocaleName` varchar(7) DEFAULT NULL,
  `Language` longtext DEFAULT NULL,
  `MenuItemId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_MenuItemTranslations_LocaleName` (`LocaleName`),
  KEY `IX_MenuItemTranslations_MenuItemId` (`MenuItemId`),
  CONSTRAINT `FK_MenuItemTranslations_MenuItems_MenuItemId` FOREIGN KEY (`MenuItemId`) REFERENCES `MenuItems` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=89 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `MenuItemTranslations`
--

LOCK TABLES `MenuItemTranslations` WRITE;
/*!40000 ALTER TABLE `MenuItemTranslations` DISABLE KEYS */;
INSERT INTO `MenuItemTranslations` VALUES (1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'My eForms','en-US','English',1),(2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Device Users','en-US','English',2),(3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Advanced','en-US','English',3),(4,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Sites','en-US','English',4),(5,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Workers','en-US','English',5),(6,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Units','en-US','English',6),(7,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Searchable list','en-US','English',7),(8,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Selectable list','en-US','English',8),(9,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Application settings','en-US','English',9),(10,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Plugins','en-US','English',10),(11,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Folders','en-US','English',11),(12,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Email recipients','en-US','English',12),(14,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Mine eForms','da','Danish',1),(15,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Mobilbrugere','da','Danish',2),(16,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Admin','da','Danish',3),(17,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Lokationer','da','Danish',4),(18,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Medarbejder','da','Danish',5),(19,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Enheder','da','Danish',6),(20,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Søgbar Lister','da','Danish',7),(21,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Valgbar Liste','da','Danish',8),(22,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Applikationsindstillinger','da','Danish',9),(23,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Plugins','da','Danish',10),(24,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Folders','da','Danish',11),(25,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'E-mail-modtagere','da','Danish',12),(27,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Meine eForms','de-DE','German',1),(28,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Gerätebenutzer ','de-DE','German',2),(29,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Fortgeschritten','de-DE','German',3),(30,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Standorte','de-DE','German',4),(31,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Mitarbeiter','de-DE','German',5),(32,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Einheiten','de-DE','German',6),(33,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Durchsuchbare Listen','de-DE','German',7),(34,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Auswählbare Liste','de-DE','German',8),(35,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Anwendungseinstellungen','de-DE','German',9),(36,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Plugins','de-DE','German',10),(37,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Folders','de-DE','German',11),(38,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'E-Mail-Empfänger','de-DE','German',12),(40,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Items Planning','en-US','English',19),(41,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Artikelplanung','de-DE','German',19),(42,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Egenkontrol','da','Danish',19),(43,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Планування елементів','uk-UA','Ukrainian',19),(44,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Planning','en-US','English',20),(45,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Planung','de-DE','German',20),(46,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Planlægning','da','Danish',20),(47,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Планування','uk-UA','Ukrainian',20),(48,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Reports','en-US','English',21),(49,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Berichte','de-DE','German',21),(50,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Rapporter','da','Danish',21),(51,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Звіти','uk-UA','Ukrainian',21),(52,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Pairing','en-US','English',22),(53,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Koppelen','de-DE','German',22),(54,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Parring','da','Danish',22),(55,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Зв\'язування','uk-UA','Ukrainian',22),(56,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Time Planning','en-US','English',23),(57,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Time Planning','de-DE','German',23),(58,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Timeregistrering','da','Danish',23),(59,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Working hours','en-US','English',24),(60,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Working hours','de-DE','German',24),(61,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Timeregistrering','da','Danish',24),(62,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Flex','en-US','English',25),(63,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Flex','de-DE','German',25),(64,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Flex','da','Danish',25),(65,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Backend Configuration','en-US','English',26),(66,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Aufbau','de-DE','German',26),(67,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Konfiguration','da','Danish',26),(68,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Конфігурація серверної частини','uk-UA','Ukrainian',26),(69,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Properties','en-US','English',27),(70,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Eigenschaften','de-DE','German',27),(71,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Ejendomme','da','Danish',27),(72,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Властивості','uk-UA','Ukrainian',27),(73,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Workers','en-US','English',28),(74,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Mitarbeiter','de-DE','German',28),(75,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Medarbejdere','da','Danish',28),(76,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'працівників','uk-UA','Ukrainian',28),(77,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Task management','en-US','English',29),(78,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Aufgabenverwaltung','de-DE','German',29),(79,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Opgavestyring','da','Danish',29),(80,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Управління завданнями','uk-UA','Ukrainian',29),(81,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Reports','en-US','English',30),(82,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Berichte','de-DE','German',30),(83,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Rapporter','da','Danish',30),(84,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Звіти','uk-UA','Ukrainian',30),(85,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Documents','en-US','English',31),(86,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Unterlagen','de-DE','German',31),(87,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Dokumenter','da','Danish',31),(88,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'Документи','uk-UA','Ukrainian',31);
/*!40000 ALTER TABLE `MenuItemTranslations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `MenuItems`
--

DROP TABLE IF EXISTS `MenuItems`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `MenuItems` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Link` longtext DEFAULT NULL,
  `Position` int(11) NOT NULL,
  `ParentId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `MenuTemplateId` int(11) DEFAULT NULL,
  `Type` int(11) NOT NULL DEFAULT 0,
  `Name` longtext DEFAULT NULL,
  `E2EId` longtext DEFAULT NULL,
  `IsInternalLink` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`Id`),
  KEY `IX_MenuItems_ParentId` (`ParentId`),
  KEY `IX_MenuItems_MenuTemplateId` (`MenuTemplateId`),
  CONSTRAINT `FK_MenuItems_MenuItems_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `MenuItems` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_MenuItems_MenuTemplates_MenuTemplateId` FOREIGN KEY (`MenuTemplateId`) REFERENCES `MenuTemplates` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB AUTO_INCREMENT=32 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `MenuItems`
--

LOCK TABLES `MenuItems` WRITE;
/*!40000 ALTER TABLE `MenuItems` DISABLE KEYS */;
INSERT INTO `MenuItems` VALUES (1,'/',0,NULL,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,1,1,'My Eforms','my-eforms',1),(2,'/device-users',1,NULL,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,2,1,'Device Users','device-users',1),(3,'',2,NULL,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,3,3,'Dropdown','advanced',1),(4,'/advanced/sites',0,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,4,1,'Sites','sites',1),(5,'/advanced/workers',1,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,5,1,'Workers','workers',1),(6,'/advanced/units',2,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,6,1,'Units','units',1),(7,'/advanced/entity-search',3,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,7,1,'Searchable list','search',1),(8,'/advanced/entity-select',4,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,8,1,'Selectable List','selectable-list',1),(9,'/application-settings',6,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,9,1,'Application settings','application-settings',1),(10,'/plugins-settings',8,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,10,1,'Plugin Settings','plugins-settings',1),(11,'/advanced/folders',5,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,11,1,'Folders','folders',1),(12,'/email-recipients',7,3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,12,1,'Email recipients','email-recipients',1),(19,'',3,NULL,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,NULL,3,'Dropdown','items-planning-pn',1),(20,'/plugins/items-planning-pn/plannings',0,19,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,14,1,'Planning','items-planning-pn-plannings',1),(21,'/plugins/items-planning-pn/reports',1,19,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,15,1,'Reports','items-planning-pn-reports',1),(22,'/plugins/items-planning-pn/pairing',2,19,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,16,1,'Pairing','items-planning-pn-pairing',1),(23,'',4,NULL,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,NULL,3,'Dropdown','time-planning-pn',1),(24,'/plugins/time-planning-pn/working-hours',0,23,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,17,1,'Working hours','time-planning-pn-working-hours',1),(25,'/plugins/time-planning-pn/flex',2,23,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,18,1,'Flex','time-planning-pn-flex',1),(26,'',5,NULL,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,NULL,3,'Dropdown','backend-configuration-pn',1),(27,'/plugins/backend-configuration-pn/properties',0,26,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,19,1,'Properties','backend-configuration-pn-properties',1),(28,'/plugins/backend-configuration-pn/property-workers',1,26,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,20,1,'Workers','backend-configuration-pn-property-workers',1),(29,'/plugins/backend-configuration-pn/task-management',1,26,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,21,1,'Task management','backend-configuration-pn-task-management',1),(30,'/plugins/backend-configuration-pn/reports',1,26,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,22,1,'Reports','backend-configuration-pn-reports',1),(31,'/plugins/backend-configuration-pn/documents',1,26,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,23,1,'Documents','backend-configuration-pn-documents',1);
/*!40000 ALTER TABLE `MenuItems` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `MenuTemplatePermissions`
--

DROP TABLE IF EXISTS `MenuTemplatePermissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `MenuTemplatePermissions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `MenuTemplateId` int(11) NOT NULL,
  `PermissionId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_MenuTemplatePermissions_MenuTemplateId_PermissionId` (`MenuTemplateId`,`PermissionId`),
  KEY `IX_MenuTemplatePermissions_PermissionId` (`PermissionId`),
  CONSTRAINT `FK_MenuTemplatePermissions_MenuTemplates_MenuTemplateId` FOREIGN KEY (`MenuTemplateId`) REFERENCES `MenuTemplates` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_MenuTemplatePermissions_Permissions_PermissionId` FOREIGN KEY (`PermissionId`) REFERENCES `Permissions` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=37 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `MenuTemplatePermissions`
--

LOCK TABLES `MenuTemplatePermissions` WRITE;
/*!40000 ALTER TABLE `MenuTemplatePermissions` DISABLE KEYS */;
INSERT INTO `MenuTemplatePermissions` VALUES (1,NULL,NULL,NULL,NULL,5,2),(2,NULL,NULL,NULL,NULL,5,1),(3,NULL,NULL,NULL,NULL,5,4),(4,NULL,NULL,NULL,NULL,5,3),(5,NULL,NULL,NULL,NULL,4,6),(6,NULL,NULL,NULL,NULL,4,8),(7,NULL,NULL,NULL,NULL,4,7),(8,NULL,NULL,NULL,NULL,7,10),(9,NULL,NULL,NULL,NULL,7,9),(10,NULL,NULL,NULL,NULL,7,12),(11,NULL,NULL,NULL,NULL,7,11),(12,NULL,NULL,NULL,NULL,7,14),(13,NULL,NULL,NULL,NULL,7,13),(14,NULL,NULL,NULL,NULL,7,16),(15,NULL,NULL,NULL,NULL,7,15),(16,NULL,NULL,NULL,NULL,6,21),(17,NULL,NULL,NULL,NULL,6,22),(18,NULL,NULL,NULL,NULL,2,24),(19,NULL,NULL,NULL,NULL,2,23),(20,NULL,NULL,NULL,NULL,2,26),(21,NULL,NULL,NULL,NULL,2,25),(22,NULL,NULL,NULL,NULL,1,27),(23,NULL,NULL,NULL,NULL,1,28),(24,NULL,NULL,NULL,NULL,1,29),(25,NULL,NULL,NULL,NULL,1,30),(26,NULL,NULL,NULL,NULL,1,31),(27,NULL,NULL,NULL,NULL,1,32),(28,NULL,NULL,NULL,NULL,1,38),(29,NULL,NULL,NULL,NULL,1,39),(30,NULL,NULL,NULL,NULL,1,40),(31,NULL,NULL,NULL,NULL,1,41),(32,NULL,NULL,NULL,NULL,1,42),(33,NULL,NULL,NULL,NULL,1,43),(34,NULL,NULL,NULL,NULL,1,44),(35,NULL,NULL,NULL,NULL,1,47),(36,NULL,NULL,NULL,NULL,14,53);
/*!40000 ALTER TABLE `MenuTemplatePermissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `MenuTemplateTranslations`
--

DROP TABLE IF EXISTS `MenuTemplateTranslations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `MenuTemplateTranslations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `Name` varchar(250) DEFAULT NULL,
  `LocaleName` varchar(7) DEFAULT NULL,
  `Language` longtext DEFAULT NULL,
  `MenuTemplateId` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_MenuTemplateTranslations_LocaleName` (`LocaleName`),
  KEY `IX_MenuTemplateTranslations_MenuTemplateId` (`MenuTemplateId`),
  CONSTRAINT `FK_MenuTemplateTranslations_MenuTemplates_MenuTemplateId` FOREIGN KEY (`MenuTemplateId`) REFERENCES `MenuTemplates` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=78 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `MenuTemplateTranslations`
--

LOCK TABLES `MenuTemplateTranslations` WRITE;
/*!40000 ALTER TABLE `MenuTemplateTranslations` DISABLE KEYS */;
INSERT INTO `MenuTemplateTranslations` VALUES (1,NULL,NULL,NULL,NULL,'My eForms','en-US','English',1),(2,NULL,NULL,NULL,NULL,'Device Users','en-US','English',2),(3,NULL,NULL,NULL,NULL,'Advanced','en-US','English',3),(4,NULL,NULL,NULL,NULL,'Sites','en-US','English',4),(5,NULL,NULL,NULL,NULL,'Workers','en-US','English',5),(6,NULL,NULL,NULL,NULL,'Units','en-US','English',6),(7,NULL,NULL,NULL,NULL,'Searchable list','en-US','English',7),(8,NULL,NULL,NULL,NULL,'Selectable list','en-US','English',8),(9,NULL,NULL,NULL,NULL,'Application settings','en-US','English',9),(10,NULL,NULL,NULL,NULL,'Plugins','en-US','English',10),(11,NULL,NULL,NULL,NULL,'Folders','en-US','English',11),(12,NULL,NULL,NULL,NULL,'Email recipients','en-US','English',12),(13,NULL,NULL,NULL,NULL,'Mine eForms','da','Danish',1),(14,NULL,NULL,NULL,NULL,'Mobilbrugere','da','Danish',2),(15,NULL,NULL,NULL,NULL,'Admin','da','Danish',3),(16,NULL,NULL,NULL,NULL,'Lokationer','da','Danish',4),(17,NULL,NULL,NULL,NULL,'Medarbejder','da','Danish',5),(18,NULL,NULL,NULL,NULL,'Enheder','da','Danish',6),(19,NULL,NULL,NULL,NULL,'Søgbar Lister','da','Danish',7),(20,NULL,NULL,NULL,NULL,'Valgbar Liste','da','Danish',8),(21,NULL,NULL,NULL,NULL,'Applikationsindstillinger','da','Danish',9),(22,NULL,NULL,NULL,NULL,'Plugins','da','Danish',10),(23,NULL,NULL,NULL,NULL,'Folders','da','Danish',11),(24,NULL,NULL,NULL,NULL,'E-mail-modtagere','da','Danish',12),(25,NULL,NULL,NULL,NULL,'Meine eForms','de-DE','German',1),(26,NULL,NULL,NULL,NULL,'Gerätebenutzer ','de-DE','German',2),(27,NULL,NULL,NULL,NULL,'Fortgeschritten','de-DE','German',3),(28,NULL,NULL,NULL,NULL,'Standorte','de-DE','German',4),(29,NULL,NULL,NULL,NULL,'Mitarbeiter','de-DE','German',5),(30,NULL,NULL,NULL,NULL,'Einheiten','de-DE','German',6),(31,NULL,NULL,NULL,NULL,'Durchsuchbare Listen','de-DE','German',7),(32,NULL,NULL,NULL,NULL,'Auswählbare Liste','de-DE','German',8),(33,NULL,NULL,NULL,NULL,'Anwendungseinstellungen','de-DE','German',9),(34,NULL,NULL,NULL,NULL,'Plugins','de-DE','German',10),(35,NULL,NULL,NULL,NULL,'Folders','de-DE','German',11),(36,NULL,NULL,NULL,NULL,'E-Mail-Empfänger','de-DE','German',12),(40,NULL,NULL,NULL,NULL,'Planning','en-US','English',14),(41,NULL,NULL,NULL,NULL,'Planung','de-DE','German',14),(42,NULL,NULL,NULL,NULL,'Planlægning','da','Danish',14),(43,NULL,NULL,NULL,NULL,'Планування','uk-UA','Ukrainian',14),(44,NULL,NULL,NULL,NULL,'Reports','en-US','English',15),(45,NULL,NULL,NULL,NULL,'Berichte','de-DE','German',15),(46,NULL,NULL,NULL,NULL,'Rapporter','da','Danish',15),(47,NULL,NULL,NULL,NULL,'Звіти','uk-UA','Ukrainian',15),(48,NULL,NULL,NULL,NULL,'Pairing','en-US','English',16),(49,NULL,NULL,NULL,NULL,'Koppelen','de-DE','German',16),(50,NULL,NULL,NULL,NULL,'Parring','da','Danish',16),(51,NULL,NULL,NULL,NULL,'Зв\'язування','uk-UA','Ukrainian',16),(52,NULL,NULL,NULL,NULL,'Working hours','en-US','English',17),(53,NULL,NULL,NULL,NULL,'Working hours','de-DE','German',17),(54,NULL,NULL,NULL,NULL,'Timeregistrering','da','Danish',17),(55,NULL,NULL,NULL,NULL,'Flex','en-US','English',18),(56,NULL,NULL,NULL,NULL,'Flex','de-DE','German',18),(57,NULL,NULL,NULL,NULL,'Flex','da','Danish',18),(58,NULL,NULL,NULL,NULL,'Properties','en-US','English',19),(59,NULL,NULL,NULL,NULL,'Eigenschaften','de-DE','German',19),(60,NULL,NULL,NULL,NULL,'Ejendomme','da','Danish',19),(61,NULL,NULL,NULL,NULL,'Властивості','uk-UA','Ukrainian',19),(62,NULL,NULL,NULL,NULL,'Workers','en-US','English',20),(63,NULL,NULL,NULL,NULL,'Mitarbeiter','de-DE','German',20),(64,NULL,NULL,NULL,NULL,'Medarbejdere','da','Danish',20),(65,NULL,NULL,NULL,NULL,'працівників','uk-UA','Ukrainian',20),(66,NULL,NULL,NULL,NULL,'Task management','en-US','English',21),(67,NULL,NULL,NULL,NULL,'Aufgabenverwaltung','de-DE','German',21),(68,NULL,NULL,NULL,NULL,'Opgavestyring','da','Danish',21),(69,NULL,NULL,NULL,NULL,'Управління завданнями','uk-UA','Ukrainian',21),(70,NULL,NULL,NULL,NULL,'Reports','en-US','English',22),(71,NULL,NULL,NULL,NULL,'Berichte','de-DE','German',22),(72,NULL,NULL,NULL,NULL,'Rapporter','da','Danish',22),(73,NULL,NULL,NULL,NULL,'Звіти','uk-UA','Ukrainian',22),(74,NULL,NULL,NULL,NULL,'Documents','en-US','English',23),(75,NULL,NULL,NULL,NULL,'Unterlagen','de-DE','German',23),(76,NULL,NULL,NULL,NULL,'Dokumenter','da','Danish',23),(77,NULL,NULL,NULL,NULL,'Документи','uk-UA','Ukrainian',23);
/*!40000 ALTER TABLE `MenuTemplateTranslations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `MenuTemplates`
--

DROP TABLE IF EXISTS `MenuTemplates`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `MenuTemplates` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `DefaultLink` longtext DEFAULT NULL,
  `E2EId` longtext DEFAULT NULL,
  `EformPluginId` int(11) DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_MenuTemplates_EformPluginId` (`EformPluginId`),
  CONSTRAINT `FK_MenuTemplates_EformPlugins_EformPluginId` FOREIGN KEY (`EformPluginId`) REFERENCES `EformPlugins` (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=24 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `MenuTemplates`
--

LOCK TABLES `MenuTemplates` WRITE;
/*!40000 ALTER TABLE `MenuTemplates` DISABLE KEYS */;
INSERT INTO `MenuTemplates` VALUES (1,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/','my-eforms',NULL,'My Eforms'),(2,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/device-users','device-users',NULL,'Device Users'),(3,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'','advanced',NULL,'Advanced'),(4,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/advanced/sites','sites',NULL,'Sites'),(5,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/advanced/workers','workers',NULL,'Workers'),(6,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/advanced/units','units',NULL,'Units'),(7,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/advanced/entity-search','search',NULL,'Searchable list'),(8,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/advanced/entity-select','selectable-list',NULL,'Selectable List'),(9,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/application-settings','application-settings',NULL,'Application settings'),(10,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins-settings','plugins-settings',NULL,'Plugins'),(11,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/advanced/folders','folders',NULL,'Folders'),(12,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/email-recipients','email-recipients',NULL,'Email recipients'),(14,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/items-planning-pn/plannings','items-planning-pn-plannings',3,'Planning'),(15,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/items-planning-pn/reports','items-planning-pn-reports',3,'Reports'),(16,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/items-planning-pn/pairing','items-planning-pn-pairing',3,'Pairing'),(17,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/time-planning-pn/working-hours','time-planning-pn-working-hours',1,'Working hours'),(18,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/time-planning-pn/flex','time-planning-pn-flex',1,'Flex'),(19,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/backend-configuration-pn/properties','backend-configuration-pn-properties',2,'Properties'),(20,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/backend-configuration-pn/property-workers','backend-configuration-pn-property-workers',2,'Workers'),(21,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/backend-configuration-pn/task-management','backend-configuration-pn-task-management',2,'Task management'),(22,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/backend-configuration-pn/reports','backend-configuration-pn-reports',2,'Reports'),(23,'0001-01-01 00:00:00.000000',NULL,NULL,0,0,0,'/plugins/backend-configuration-pn/documents','backend-configuration-pn-documents',2,'Documents');
/*!40000 ALTER TABLE `MenuTemplates` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `PermissionTypes`
--

DROP TABLE IF EXISTS `PermissionTypes`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `PermissionTypes` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` varchar(250) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_PermissionTypes_Name` (`Name`)
) ENGINE=InnoDB AUTO_INCREMENT=12 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PermissionTypes`
--

LOCK TABLES `PermissionTypes` WRITE;
/*!40000 ALTER TABLE `PermissionTypes` DISABLE KEYS */;
INSERT INTO `PermissionTypes` VALUES (1,'Workers','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(2,'Sites','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(3,'Entity search','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(4,'Entity select','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(5,'User management','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(6,'Units','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(7,'Device users','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(8,'Cases','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(9,'Eforms','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(10,'EmailRecipients','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(11,'Plannings','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL);
/*!40000 ALTER TABLE `PermissionTypes` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Permissions`
--

DROP TABLE IF EXISTS `Permissions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Permissions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `PermissionName` varchar(250) DEFAULT NULL,
  `ClaimName` varchar(250) DEFAULT NULL,
  `PermissionTypeId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Permissions_ClaimName` (`ClaimName`),
  KEY `IX_Permissions_PermissionTypeId` (`PermissionTypeId`),
  CONSTRAINT `FK_Permissions_PermissionTypes_PermissionTypeId` FOREIGN KEY (`PermissionTypeId`) REFERENCES `PermissionTypes` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=54 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Permissions`
--

LOCK TABLES `Permissions` WRITE;
/*!40000 ALTER TABLE `Permissions` DISABLE KEYS */;
INSERT INTO `Permissions` VALUES (1,'Create','workers_create',1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(2,'Read','workers_read',1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(3,'Update','workers_update',1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(4,'Delete','workers_delete',1,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(6,'Read','sites_read',2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(7,'Update','sites_update',2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(8,'Delete','sites_delete',2,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(9,'Create','entity_search_create',3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(10,'Read','entity_search_read',3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(11,'Update','entity_search_update',3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(12,'Delete','entity_search_delete',3,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(13,'Create','entity_select_create',4,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(14,'Read','entity_select_read',4,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(15,'Update','entity_select_update',4,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(16,'Delete','entity_select_delete',4,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(17,'Create','users_create',5,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(18,'Read','users_read',5,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(19,'Update','users_update',5,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(20,'Delete','users_delete',5,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(21,'Read','units_read',6,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(22,'Update','units_update',6,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(23,'Create','device_users_create',7,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(24,'Read','device_users_read',7,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(25,'Update','device_users_update',7,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(26,'Delete','device_users_delete',7,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(27,'Create','eforms_create',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(28,'Delete','eforms_delete',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(29,'Read','eforms_read',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(30,'Update columns','eforms_update_columns',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(31,'Download XML','eforms_download_xml',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(32,'Upload ZIP','eforms_upload_zip',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(33,'Cases read','cases_read',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(34,'Case read','case_read',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(35,'Case update','case_update',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(36,'Case delete','case_delete',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(37,'Get PDF','case_get_pdf',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(38,'Pairing read','eforms_pairing_read',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(39,'Pairing update','eforms_pairing_update',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(40,'Read tags','eforms_read_tags',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(41,'Update tags','eforms_update_tags',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(42,'Get CSV','eforms_get_csv',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(43,'Read Jasper Report','eforms_read_jasper_report',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(44,'Update Jasper Report','eforms_update_jasper_report',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(45,'Get DOCX','case_get_docx',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(46,'Get PPTX','case_get_pptx',8,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(47,'Export eForm excel','eform_export_eform_excel',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(49,'Create e-mail recipients','email_recipient_create',10,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(50,'Delete e-mail recipients','email_recipient_delete',10,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(51,'Read e-mail recipients','email_recipient_read',10,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(52,'Allow managing eform tags','eform_allow_managing_eform_tags',9,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL),(53,'Obtain plannings','plannings_get',11,'0001-01-01 00:00:00.000000',0,NULL,0,0,NULL);
/*!40000 ALTER TABLE `Permissions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `RoleClaims`
--

DROP TABLE IF EXISTS `RoleClaims`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `RoleClaims` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `RoleId` int(11) NOT NULL,
  `ClaimType` longtext DEFAULT NULL,
  `ClaimValue` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_RoleClaims_RoleId` (`RoleId`),
  CONSTRAINT `FK_RoleClaims_Roles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `Roles` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `RoleClaims`
--

LOCK TABLES `RoleClaims` WRITE;
/*!40000 ALTER TABLE `RoleClaims` DISABLE KEYS */;
/*!40000 ALTER TABLE `RoleClaims` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Roles`
--

DROP TABLE IF EXISTS `Roles`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Roles` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` varchar(256) DEFAULT NULL,
  `NormalizedName` varchar(256) DEFAULT NULL,
  `ConcurrencyStamp` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `RoleNameIndex` (`NormalizedName`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Roles`
--

LOCK TABLES `Roles` WRITE;
/*!40000 ALTER TABLE `Roles` DISABLE KEYS */;
INSERT INTO `Roles` VALUES (1,'admin','admin',NULL),(2,'user','user',NULL);
/*!40000 ALTER TABLE `Roles` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `SavedTags`
--

DROP TABLE IF EXISTS `SavedTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `SavedTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `TagId` int(11) NOT NULL,
  `TagName` varchar(250) DEFAULT NULL,
  `EformUserId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_SavedTags_EformUserId_TagId` (`EformUserId`,`TagId`),
  CONSTRAINT `FK_SavedTags_Users_EformUserId` FOREIGN KEY (`EformUserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `SavedTags`
--

LOCK TABLES `SavedTags` WRITE;
/*!40000 ALTER TABLE `SavedTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `SavedTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `SecurityGroupUsers`
--

DROP TABLE IF EXISTS `SecurityGroupUsers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `SecurityGroupUsers` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `SecurityGroupId` int(11) NOT NULL,
  `EformUserId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_SecurityGroupUsers_EformUserId_SecurityGroupId` (`EformUserId`,`SecurityGroupId`),
  KEY `IX_SecurityGroupUsers_EformUserId` (`EformUserId`),
  KEY `IX_SecurityGroupUsers_SecurityGroupId` (`SecurityGroupId`),
  CONSTRAINT `FK_SecurityGroupUsers_SecurityGroups_SecurityGroupId` FOREIGN KEY (`SecurityGroupId`) REFERENCES `SecurityGroups` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_SecurityGroupUsers_Users_EformUserId` FOREIGN KEY (`EformUserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `SecurityGroupUsers`
--

LOCK TABLES `SecurityGroupUsers` WRITE;
/*!40000 ALTER TABLE `SecurityGroupUsers` DISABLE KEYS */;
/*!40000 ALTER TABLE `SecurityGroupUsers` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `SecurityGroups`
--

DROP TABLE IF EXISTS `SecurityGroups`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `SecurityGroups` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` varchar(250) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000',
  `CreatedByUserId` int(11) NOT NULL DEFAULT 0,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `UpdatedByUserId` int(11) NOT NULL DEFAULT 0,
  `Version` int(11) NOT NULL DEFAULT 0,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `RedirectLink` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `SecurityGroups`
--

LOCK TABLES `SecurityGroups` WRITE;
/*!40000 ALTER TABLE `SecurityGroups` DISABLE KEYS */;
INSERT INTO `SecurityGroups` VALUES (1,'eForm admins','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,NULL),(2,'eForm users','0001-01-01 00:00:00.000000',0,NULL,0,0,NULL,NULL);
/*!40000 ALTER TABLE `SecurityGroups` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UserClaims`
--

DROP TABLE IF EXISTS `UserClaims`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UserClaims` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `ClaimType` longtext DEFAULT NULL,
  `ClaimValue` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_UserClaims_UserId` (`UserId`),
  CONSTRAINT `FK_UserClaims_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `UserClaims`
--

LOCK TABLES `UserClaims` WRITE;
/*!40000 ALTER TABLE `UserClaims` DISABLE KEYS */;
/*!40000 ALTER TABLE `UserClaims` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UserLogins`
--

DROP TABLE IF EXISTS `UserLogins`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UserLogins` (
  `LoginProvider` varchar(255) NOT NULL,
  `ProviderKey` varchar(255) NOT NULL,
  `ProviderDisplayName` longtext DEFAULT NULL,
  `UserId` int(11) NOT NULL,
  PRIMARY KEY (`LoginProvider`,`ProviderKey`),
  KEY `IX_UserLogins_UserId` (`UserId`),
  CONSTRAINT `FK_UserLogins_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `UserLogins`
--

LOCK TABLES `UserLogins` WRITE;
/*!40000 ALTER TABLE `UserLogins` DISABLE KEYS */;
/*!40000 ALTER TABLE `UserLogins` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UserRoles`
--

DROP TABLE IF EXISTS `UserRoles`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UserRoles` (
  `UserId` int(11) NOT NULL,
  `RoleId` int(11) NOT NULL,
  PRIMARY KEY (`UserId`,`RoleId`),
  KEY `IX_UserRoles_RoleId` (`RoleId`),
  CONSTRAINT `FK_UserRoles_Roles_RoleId` FOREIGN KEY (`RoleId`) REFERENCES `Roles` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_UserRoles_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `UserRoles`
--

LOCK TABLES `UserRoles` WRITE;
/*!40000 ALTER TABLE `UserRoles` DISABLE KEYS */;
INSERT INTO `UserRoles` VALUES (1,1);
/*!40000 ALTER TABLE `UserRoles` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `UserTokens`
--

DROP TABLE IF EXISTS `UserTokens`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `UserTokens` (
  `UserId` int(11) NOT NULL,
  `LoginProvider` varchar(255) NOT NULL,
  `Name` varchar(255) NOT NULL,
  `Value` longtext DEFAULT NULL,
  PRIMARY KEY (`UserId`,`LoginProvider`,`Name`),
  CONSTRAINT `FK_UserTokens_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `UserTokens`
--

LOCK TABLES `UserTokens` WRITE;
/*!40000 ALTER TABLE `UserTokens` DISABLE KEYS */;
/*!40000 ALTER TABLE `UserTokens` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Users`
--

DROP TABLE IF EXISTS `Users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Users` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `UserName` varchar(256) DEFAULT NULL,
  `NormalizedUserName` varchar(256) DEFAULT NULL,
  `Email` varchar(256) DEFAULT NULL,
  `NormalizedEmail` varchar(256) DEFAULT NULL,
  `EmailConfirmed` tinyint(1) NOT NULL,
  `PasswordHash` longtext DEFAULT NULL,
  `SecurityStamp` longtext DEFAULT NULL,
  `ConcurrencyStamp` longtext DEFAULT NULL,
  `PhoneNumber` longtext DEFAULT NULL,
  `PhoneNumberConfirmed` tinyint(1) NOT NULL,
  `TwoFactorEnabled` tinyint(1) NOT NULL,
  `LockoutEnd` datetime(6) DEFAULT NULL,
  `LockoutEnabled` tinyint(1) NOT NULL,
  `AccessFailedCount` int(11) NOT NULL,
  `FirstName` longtext DEFAULT NULL,
  `LastName` longtext DEFAULT NULL,
  `Locale` longtext DEFAULT NULL,
  `IsGoogleAuthenticatorEnabled` tinyint(1) NOT NULL,
  `GoogleAuthenticatorSecretKey` longtext DEFAULT NULL,
  `DarkTheme` tinyint(1) NOT NULL DEFAULT 0,
  `Formats` longtext DEFAULT NULL,
  `TimeZone` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UserNameIndex` (`NormalizedUserName`),
  KEY `EmailIndex` (`NormalizedEmail`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Users`
--

LOCK TABLES `Users` WRITE;
/*!40000 ALTER TABLE `Users` DISABLE KEYS */;
INSERT INTO `Users` VALUES (1,'admin@admin.com','ADMIN@ADMIN.COM','admin@admin.com','ADMIN@ADMIN.COM',1,'AQAAAAIAAYagAAAAEBSQNPHEbBBRiDYgPC/kpK5Qq7rOZ/Pe36+sY+e/CRyVrSNR6dpuW43kBrxdxcu/ug==','22ZVEUBNCOQC573ZHN4DXMMIXCRGVMH3','b059e043-9d8e-4ea2-b161-8a60890a1811',NULL,0,0,NULL,1,0,'John','Smith','da',0,NULL,1,'de-DE','Europe/Copenhagen');
/*!40000 ALTER TABLE `Users` ENABLE KEYS */;
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
INSERT INTO `__EFMigrationsHistory` VALUES ('20180916221611_Init','7.0.2'),('20181023124217_AddSecurityGroupsTables','7.0.2'),('20181026150448_AddPermissionsTables','7.0.2'),('20181101123321_AddEformPermissionsTables','7.0.2'),('20181101132520_AddSeedData','7.0.2'),('20181101211626_UpdateDefaultGroupPermission','7.0.2'),('20181115144143_AddSavedTagsTable','7.0.2'),('20181219124610_AddLocaleName','7.0.2'),('20181219165321_MenuUpdate','7.0.2'),('20181225130039_AddDefaultSettingsData','7.0.2'),('20181225201919_AddReportsTables','7.0.2'),('20190103132124_AddPluginTables','7.0.2'),('20190204191056_AddJasperPolicies','7.0.2'),('20190204193739_FixJasperReportClaimsName','7.0.2'),('20190312204124_UpdateConfigurationData','7.0.2'),('20190314094743_AddingNewAttributes','7.0.2'),('20190404090013_AddingNewPermissions','7.0.2'),('20190408060348_AddingFoldersMenu','7.0.2'),('20190408071807_FixingPathForFolders','7.0.2'),('20191010073421_RenamingSimpleSites','7.0.2'),('20200311194934_AddMailingTables','7.0.2'),('20200318122739_AddMenuItemsAndCaseId','7.0.2'),('20200319142538_AddTitleToCasePosts','7.0.2'),('20200320183418_AddSendGridKeyToConfiguration','7.0.2'),('20200324175234_RemoveFromTitleFromCase','7.0.2'),('20200424080654_AddingNewAttributesToUser','7.0.2'),('20200629170055_AddedEformExcelPermission','7.0.2'),('20201006142508_AddSecurityGroupRedirectLink','7.0.2'),('20201010162618_AddingNewSeeds','7.0.2'),('20201027012721_AddedNavigationMenuNewScheme','7.0.2'),('20201027092906_ChangedLocalNameForDanish','7.0.2'),('20201027173127_SeedMenuTemplateItemName','7.0.2'),('20201028125639_ChangedNavigationMenuItemModel','7.0.2'),('20201029124822_AddedMenuEditorItemToDefaultMenu','7.0.2'),('20201030151056_AddedE2EIdToMenuItems','7.0.2'),('20201103114612_RefreshDatabase','7.0.2'),('20201209182916_MoveMenuEditorToRightMenu','7.0.2'),('20201209183100_RemoveMenuTemplateFromLeftMenu','7.0.2'),('20210624151155_AddPermissionForEformManagingTagsButton','7.0.2'),('20220128112451_AddUserbackTokenToSetting','7.0.2'),('20220221132432_AddInternalExternalLinkInMenu','7.0.2');
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

-- Dump completed on 2023-02-08 16:04:31
