-- MariaDB dump 10.19  Distrib 10.6.11-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: localhost    Database: 420_eform-angular-case-template-plugin
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
-- Table structure for table `CaseVersions`
--

DROP TABLE IF EXISTS `CaseVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `CaseVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Status` int(11) DEFAULT NULL,
  `DoneAt` datetime(6) DEFAULT NULL,
  `SiteId` int(11) DEFAULT NULL,
  `UnitId` int(11) DEFAULT NULL,
  `WorkerId` int(11) DEFAULT NULL,
  `eFormId` int(11) DEFAULT NULL,
  `Type` varchar(255) DEFAULT NULL,
  `CaseTemplateId` int(11) NOT NULL,
  `DocumentId` int(11) DEFAULT NULL,
  `FetchedByTablet` tinyint(1) NOT NULL,
  `FetchedByTabletAt` datetime(6) NOT NULL,
  `ReceiptRetrievedFromUser` tinyint(1) NOT NULL,
  `ReceiptRetrievedFromUserAt` datetime(6) NOT NULL,
  `CaseId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_CaseVersions_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_CaseVersions_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `CaseVersions`
--

LOCK TABLES `CaseVersions` WRITE;
/*!40000 ALTER TABLE `CaseVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `CaseVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Cases`
--

DROP TABLE IF EXISTS `Cases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Cases` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Status` int(11) DEFAULT NULL,
  `DoneAt` datetime(6) DEFAULT NULL,
  `SiteId` int(11) DEFAULT NULL,
  `UnitId` int(11) DEFAULT NULL,
  `WorkerId` int(11) DEFAULT NULL,
  `eFormId` int(11) DEFAULT NULL,
  `Type` varchar(255) DEFAULT NULL,
  `CaseTemplateId` int(11) NOT NULL,
  `DocumentId` int(11) DEFAULT NULL,
  `FetchedByTablet` tinyint(1) NOT NULL,
  `FetchedByTabletAt` datetime(6) NOT NULL,
  `ReceiptRetrievedFromUser` tinyint(1) NOT NULL,
  `ReceiptRetrievedFromUserAt` datetime(6) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Cases_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_Cases_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Cases`
--

LOCK TABLES `Cases` WRITE;
/*!40000 ALTER TABLE `Cases` DISABLE KEYS */;
/*!40000 ALTER TABLE `Cases` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentProperties`
--

DROP TABLE IF EXISTS `DocumentProperties`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentProperties` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `ExpireDate` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_DocumentProperties_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_DocumentProperties_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentProperties`
--

LOCK TABLES `DocumentProperties` WRITE;
/*!40000 ALTER TABLE `DocumentProperties` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentProperties` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentPropertyVersions`
--

DROP TABLE IF EXISTS `DocumentPropertyVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentPropertyVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `DocumentPropertyId` int(11) NOT NULL,
  `Version` int(11) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedAt` datetime(6) DEFAULT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `ExpireDate` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentPropertyVersions`
--

LOCK TABLES `DocumentPropertyVersions` WRITE;
/*!40000 ALTER TABLE `DocumentPropertyVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentPropertyVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentSiteTagVersions`
--

DROP TABLE IF EXISTS `DocumentSiteTagVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentSiteTagVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `SdkSiteTagId` int(11) NOT NULL,
  `DocumentSiteTagId` int(11) NOT NULL,
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
-- Dumping data for table `DocumentSiteTagVersions`
--

LOCK TABLES `DocumentSiteTagVersions` WRITE;
/*!40000 ALTER TABLE `DocumentSiteTagVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentSiteTagVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentSiteTags`
--

DROP TABLE IF EXISTS `DocumentSiteTags`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentSiteTags` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `SdkSiteTagId` int(11) NOT NULL,
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
-- Dumping data for table `DocumentSiteTags`
--

LOCK TABLES `DocumentSiteTags` WRITE;
/*!40000 ALTER TABLE `DocumentSiteTags` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentSiteTags` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentSiteVersions`
--

DROP TABLE IF EXISTS `DocumentSiteVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentSiteVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `SdkSiteId` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `DocumentSiteId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentSiteVersions`
--

LOCK TABLES `DocumentSiteVersions` WRITE;
/*!40000 ALTER TABLE `DocumentSiteVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentSiteVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentSites`
--

DROP TABLE IF EXISTS `DocumentSites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentSites` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `SdkSiteId` int(11) NOT NULL,
  `SdkCaseId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_DocumentSites_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_DocumentSites_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentSites`
--

LOCK TABLES `DocumentSites` WRITE;
/*!40000 ALTER TABLE `DocumentSites` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentSites` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentTranslationVersions`
--

DROP TABLE IF EXISTS `DocumentTranslationVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentTranslationVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentTranslation` int(11) NOT NULL,
  `DocumentId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `PdfTitle` longtext DEFAULT NULL,
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
-- Dumping data for table `DocumentTranslationVersions`
--

LOCK TABLES `DocumentTranslationVersions` WRITE;
/*!40000 ALTER TABLE `DocumentTranslationVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentTranslationVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentTranslations`
--

DROP TABLE IF EXISTS `DocumentTranslations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentTranslations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `PdfTitle` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_DocumentTranslations_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_DocumentTranslations_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentTranslations`
--

LOCK TABLES `DocumentTranslations` WRITE;
/*!40000 ALTER TABLE `DocumentTranslations` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentTranslations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentUploadedDataVersions`
--

DROP TABLE IF EXISTS `DocumentUploadedDataVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentUploadedDataVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `File` longtext DEFAULT NULL,
  `Approvable` tinyint(1) NOT NULL,
  `RetractIfApproved` tinyint(1) NOT NULL,
  `DocumentUploadedDataId` int(11) NOT NULL,
  `LanguageId` int(11) NOT NULL,
  `Hash` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `SdkHash` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_DocumentUploadedDataVersions_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_DocumentUploadedDataVersions_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentUploadedDataVersions`
--

LOCK TABLES `DocumentUploadedDataVersions` WRITE;
/*!40000 ALTER TABLE `DocumentUploadedDataVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentUploadedDataVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentUploadedDatas`
--

DROP TABLE IF EXISTS `DocumentUploadedDatas`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentUploadedDatas` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DocumentId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `File` longtext DEFAULT NULL,
  `Approvable` tinyint(1) NOT NULL,
  `RetractIfApproved` tinyint(1) NOT NULL,
  `LanguageId` int(11) NOT NULL,
  `Hash` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `SdkHash` longtext DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_DocumentUploadedDatas_DocumentId` (`DocumentId`),
  CONSTRAINT `FK_DocumentUploadedDatas_Documents_DocumentId` FOREIGN KEY (`DocumentId`) REFERENCES `Documents` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentUploadedDatas`
--

LOCK TABLES `DocumentUploadedDatas` WRITE;
/*!40000 ALTER TABLE `DocumentUploadedDatas` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentUploadedDatas` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DocumentVersions`
--

DROP TABLE IF EXISTS `DocumentVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DocumentVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Title` longtext DEFAULT NULL,
  `Body` longtext DEFAULT NULL,
  `StartAt` datetime(6) NOT NULL,
  `EndAt` datetime(6) NOT NULL,
  `PdfTitle` longtext DEFAULT NULL,
  `Approvable` tinyint(1) NOT NULL,
  `RetractIfApproved` tinyint(1) NOT NULL,
  `AlwaysShow` tinyint(1) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `DocumentId` int(11) NOT NULL,
  `Status` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DocumentVersions`
--

LOCK TABLES `DocumentVersions` WRITE;
/*!40000 ALTER TABLE `DocumentVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DocumentVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Documents`
--

DROP TABLE IF EXISTS `Documents`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Documents` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `StartAt` datetime(6) NOT NULL,
  `EndAt` datetime(6) NOT NULL,
  `Approvable` tinyint(1) NOT NULL,
  `RetractIfApproved` tinyint(1) NOT NULL,
  `AlwaysShow` tinyint(1) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `Status` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Documents`
--

LOCK TABLES `Documents` WRITE;
/*!40000 ALTER TABLE `Documents` DISABLE KEYS */;
/*!40000 ALTER TABLE `Documents` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FolderProperties`
--

DROP TABLE IF EXISTS `FolderProperties`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FolderProperties` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FolderId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `SdkFolderId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_FolderProperties_FolderId` (`FolderId`),
  CONSTRAINT `FK_FolderProperties_Folders_FolderId` FOREIGN KEY (`FolderId`) REFERENCES `Folders` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FolderProperties`
--

LOCK TABLES `FolderProperties` WRITE;
/*!40000 ALTER TABLE `FolderProperties` DISABLE KEYS */;
/*!40000 ALTER TABLE `FolderProperties` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FolderPropertyVersions`
--

DROP TABLE IF EXISTS `FolderPropertyVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FolderPropertyVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FolderPropertyId` int(11) NOT NULL,
  `FolderId` int(11) NOT NULL,
  `PropertyId` int(11) NOT NULL,
  `SdkFolderId` int(11) NOT NULL,
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
-- Dumping data for table `FolderPropertyVersions`
--

LOCK TABLES `FolderPropertyVersions` WRITE;
/*!40000 ALTER TABLE `FolderPropertyVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `FolderPropertyVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FolderTranslationVersions`
--

DROP TABLE IF EXISTS `FolderTranslationVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FolderTranslationVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FolderTranslationId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
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
-- Dumping data for table `FolderTranslationVersions`
--

LOCK TABLES `FolderTranslationVersions` WRITE;
/*!40000 ALTER TABLE `FolderTranslationVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `FolderTranslationVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FolderTranslations`
--

DROP TABLE IF EXISTS `FolderTranslations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FolderTranslations` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `FolderId` int(11) NOT NULL,
  `Name` longtext DEFAULT NULL,
  `Description` longtext DEFAULT NULL,
  `LanguageId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_FolderTranslations_FolderId` (`FolderId`),
  CONSTRAINT `FK_FolderTranslations_Folders_FolderId` FOREIGN KEY (`FolderId`) REFERENCES `Folders` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `FolderTranslations`
--

LOCK TABLES `FolderTranslations` WRITE;
/*!40000 ALTER TABLE `FolderTranslations` DISABLE KEYS */;
/*!40000 ALTER TABLE `FolderTranslations` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `FolderVersions`
--

DROP TABLE IF EXISTS `FolderVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `FolderVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ParentId` int(11) DEFAULT NULL,
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
-- Dumping data for table `FolderVersions`
--

LOCK TABLES `FolderVersions` WRITE;
/*!40000 ALTER TABLE `FolderVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `FolderVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Folders`
--

DROP TABLE IF EXISTS `Folders`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Folders` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ParentId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) DEFAULT NULL,
  `WorkflowState` varchar(255) DEFAULT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Folders_ParentId` (`ParentId`),
  CONSTRAINT `FK_Folders_Folders_ParentId` FOREIGN KEY (`ParentId`) REFERENCES `Folders` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Folders`
--

LOCK TABLES `Folders` WRITE;
/*!40000 ALTER TABLE `Folders` DISABLE KEYS */;
/*!40000 ALTER TABLE `Folders` ENABLE KEYS */;
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginConfigurationValues`
--

LOCK TABLES `PluginConfigurationValues` WRITE;
/*!40000 ALTER TABLE `PluginConfigurationValues` DISABLE KEYS */;
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissionVersions`
--

LOCK TABLES `PluginGroupPermissionVersions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissionVersions` DISABLE KEYS */;
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginGroupPermissions`
--

LOCK TABLES `PluginGroupPermissions` WRITE;
/*!40000 ALTER TABLE `PluginGroupPermissions` DISABLE KEYS */;
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
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `PluginPermissions`
--

LOCK TABLES `PluginPermissions` WRITE;
/*!40000 ALTER TABLE `PluginPermissions` DISABLE KEYS */;
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
INSERT INTO `__EFMigrationsHistory` VALUES ('20220927154558_InitialMigration','7.0.2'),('20220927163153_AddingPropertyIdToDocumentSite','7.0.2'),('20220927184750_AddingSdkHashToUploadedData','7.0.2'),('20221011150253_AddingExpireDateToDocumentProperty','7.0.2'),('20221123022410_AddingIsLockedToDocument','7.0.2');
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

-- Dump completed on 2023-02-08 16:05:13
