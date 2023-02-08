-- MariaDB dump 10.19  Distrib 10.6.11-MariaDB, for debian-linux-gnu (x86_64)
--
-- Host: localhost    Database: 420_chemical-base-plugin
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
-- Table structure for table `ActiveSubstanceVersions`
--

DROP TABLE IF EXISTS `ActiveSubstanceVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ActiveSubstanceVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `RemoteId` longtext NOT NULL,
  `ChemicalId` int(11) NOT NULL,
  `ActiveSubstanceId` int(11) NOT NULL,
  `Name` longtext NOT NULL,
  `CASNo` longtext DEFAULT NULL,
  `Concentration` double DEFAULT NULL,
  `Unit` int(11) DEFAULT NULL,
  `ActionMechanism` int(11) DEFAULT NULL,
  `CotyledonousWeedType` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ActiveSubstanceVersions`
--

LOCK TABLES `ActiveSubstanceVersions` WRITE;
/*!40000 ALTER TABLE `ActiveSubstanceVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ActiveSubstanceVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ActiveSubstances`
--

DROP TABLE IF EXISTS `ActiveSubstances`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ActiveSubstances` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `RemoteId` longtext NOT NULL,
  `ChemicalId` int(11) NOT NULL,
  `CASNo` longtext DEFAULT NULL,
  `Name` longtext NOT NULL,
  `Concentration` double DEFAULT NULL,
  `Unit` int(11) DEFAULT NULL,
  `ActionMechanism` int(11) DEFAULT NULL,
  `CotyledonousWeedType` int(11) DEFAULT NULL,
  `ChemicalVersionId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_ActiveSubstances_ChemicalId` (`ChemicalId`),
  KEY `IX_ActiveSubstances_ChemicalVersionId` (`ChemicalVersionId`),
  CONSTRAINT `FK_ActiveSubstances_ChemicalVersions_ChemicalVersionId` FOREIGN KEY (`ChemicalVersionId`) REFERENCES `ChemicalVersions` (`Id`),
  CONSTRAINT `FK_ActiveSubstances_Chemicals_ChemicalId` FOREIGN KEY (`ChemicalId`) REFERENCES `Chemicals` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ActiveSubstances`
--

LOCK TABLES `ActiveSubstances` WRITE;
/*!40000 ALTER TABLE `ActiveSubstances` DISABLE KEYS */;
/*!40000 ALTER TABLE `ActiveSubstances` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AddressVersions`
--

DROP TABLE IF EXISTS `AddressVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AddressVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `StreetName` longtext DEFAULT NULL,
  `StreetBuildingNo` longtext DEFAULT NULL,
  `Floor` longtext DEFAULT NULL,
  `Room` longtext DEFAULT NULL,
  `PostalCode` longtext DEFAULT NULL,
  `CityName` longtext DEFAULT NULL,
  `DistricName` longtext DEFAULT NULL,
  `Country` int(11) NOT NULL,
  `PostOfficeBox` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AddressVersions`
--

LOCK TABLES `AddressVersions` WRITE;
/*!40000 ALTER TABLE `AddressVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AddressVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Addresses`
--

DROP TABLE IF EXISTS `Addresses`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Addresses` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `StreetName` longtext DEFAULT NULL,
  `StreetBuildingNo` longtext DEFAULT NULL,
  `Floor` longtext DEFAULT NULL,
  `Room` longtext DEFAULT NULL,
  `PostalCode` longtext DEFAULT NULL,
  `CityName` longtext DEFAULT NULL,
  `DistricName` longtext DEFAULT NULL,
  `Country` int(11) NOT NULL,
  `PostOfficeBox` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Addresses`
--

LOCK TABLES `Addresses` WRITE;
/*!40000 ALTER TABLE `Addresses` DISABLE KEYS */;
/*!40000 ALTER TABLE `Addresses` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AuthorisationHolderVersions`
--

DROP TABLE IF EXISTS `AuthorisationHolderVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AuthorisationHolderVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `RemoteId` longtext NOT NULL,
  `Name` longtext DEFAULT NULL,
  `AddressId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AuthorisationHolderVersions`
--

LOCK TABLES `AuthorisationHolderVersions` WRITE;
/*!40000 ALTER TABLE `AuthorisationHolderVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `AuthorisationHolderVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `AuthorisationHolders`
--

DROP TABLE IF EXISTS `AuthorisationHolders`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `AuthorisationHolders` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `RemoteId` longtext NOT NULL,
  `Name` longtext DEFAULT NULL,
  `AddressId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AuthorisationHolders_AddressId` (`AddressId`),
  CONSTRAINT `FK_AuthorisationHolders_Addresses_AddressId` FOREIGN KEY (`AddressId`) REFERENCES `Addresses` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `AuthorisationHolders`
--

LOCK TABLES `AuthorisationHolders` WRITE;
/*!40000 ALTER TABLE `AuthorisationHolders` DISABLE KEYS */;
/*!40000 ALTER TABLE `AuthorisationHolders` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ChemicalVersions`
--

DROP TABLE IF EXISTS `ChemicalVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ChemicalVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalId` int(11) NOT NULL,
  `Name` longtext NOT NULL,
  `RegistrationNo` longtext NOT NULL,
  `PestControlType` int(11) DEFAULT NULL,
  `Status` int(11) DEFAULT NULL,
  `PesticideAuthorisationType` int(11) DEFAULT NULL,
  `BiocideAuthorisationType` int(11) DEFAULT NULL,
  `AuthorisationHolderId` int(11) DEFAULT NULL,
  `AuthorisationDate` datetime(6) DEFAULT NULL,
  `AuthorisationExpirationDate` datetime(6) DEFAULT NULL,
  `AuthorisationTerminationDate` datetime(6) DEFAULT NULL,
  `SalesDeadline` datetime(6) DEFAULT NULL,
  `UseAndPossesionDeadline` datetime(6) DEFAULT NULL,
  `PossessionDeadline` datetime(6) DEFAULT NULL,
  `FormulationType` int(11) DEFAULT NULL,
  `FormulationSubType` int(11) DEFAULT NULL,
  `PesticideUser` int(11) DEFAULT NULL,
  `BiocideUser` longtext NOT NULL,
  `PesticideProductGroup` longtext NOT NULL,
  `ActiveSubstanceType` int(11) DEFAULT NULL,
  `Use` longtext DEFAULT NULL,
  `PesticidePossibleUse` longtext NOT NULL,
  `BiocidePossibleUse` longtext NOT NULL,
  `BiocideSpecialUse` longtext NOT NULL,
  `ClassificationAndLabelingId` int(11) NOT NULL,
  `BarcodeValue` longtext NOT NULL,
  `LastUpdatedDate` datetime(6) DEFAULT NULL,
  `RemoteId` longtext NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Verified` tinyint(1) NOT NULL DEFAULT 0,
  `BiocideProductGroup` int(11) DEFAULT NULL,
  `BiocideProductType` longtext NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ChemicalVersions`
--

LOCK TABLES `ChemicalVersions` WRITE;
/*!40000 ALTER TABLE `ChemicalVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ChemicalVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Chemicals`
--

DROP TABLE IF EXISTS `Chemicals`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Chemicals` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `Name` longtext NOT NULL,
  `RegistrationNo` longtext NOT NULL,
  `PestControlType` int(11) DEFAULT NULL,
  `Status` int(11) DEFAULT NULL,
  `PesticideAuthorisationType` int(11) DEFAULT NULL,
  `BiocideAuthorisationType` int(11) DEFAULT NULL,
  `AuthorisationHolderId` int(11) DEFAULT NULL,
  `AuthorisationDate` datetime(6) DEFAULT NULL,
  `AuthorisationExpirationDate` datetime(6) DEFAULT NULL,
  `AuthorisationTerminationDate` datetime(6) DEFAULT NULL,
  `SalesDeadline` datetime(6) DEFAULT NULL,
  `UseAndPossesionDeadline` datetime(6) DEFAULT NULL,
  `PossessionDeadline` datetime(6) DEFAULT NULL,
  `FormulationType` int(11) DEFAULT NULL,
  `FormulationSubType` int(11) DEFAULT NULL,
  `PesticideUser` int(11) DEFAULT NULL,
  `BiocideUser` longtext NOT NULL,
  `PesticideProductGroup` longtext NOT NULL,
  `ActiveSubstanceType` int(11) DEFAULT NULL,
  `Use` longtext DEFAULT NULL,
  `PesticidePossibleUse` longtext NOT NULL,
  `BiocidePossibleUse` longtext NOT NULL,
  `BiocideSpecialUse` longtext NOT NULL,
  `ClassificationAndLabelingId` int(11) NOT NULL,
  `BarcodeValue` longtext NOT NULL,
  `LastUpdatedDate` datetime(6) DEFAULT NULL,
  `RemoteId` longtext NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Verified` tinyint(1) NOT NULL DEFAULT 0,
  `BiocideProductGroup` int(11) DEFAULT NULL,
  `BiocideProductType` longtext NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Chemicals_ClassificationAndLabelingId` (`ClassificationAndLabelingId`),
  KEY `IX_Chemicals_AuthorisationHolderId` (`AuthorisationHolderId`),
  CONSTRAINT `FK_Chemicals_AuthorisationHolders_AuthorisationHolderId` FOREIGN KEY (`AuthorisationHolderId`) REFERENCES `AuthorisationHolders` (`Id`),
  CONSTRAINT `FK_Chemicals_ClassificationAndLabelings_ClassificationAndLabeli~` FOREIGN KEY (`ClassificationAndLabelingId`) REFERENCES `ClassificationAndLabelings` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Chemicals`
--

LOCK TABLES `Chemicals` WRITE;
/*!40000 ALTER TABLE `Chemicals` DISABLE KEYS */;
/*!40000 ALTER TABLE `Chemicals` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ClassificationAndLabelingVersions`
--

DROP TABLE IF EXISTS `ClassificationAndLabelingVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ClassificationAndLabelingVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ClassificationAndLabelingId` int(11) NOT NULL,
  `CLPId` int(11) NOT NULL,
  `DPDId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ClassificationAndLabelingVersions`
--

LOCK TABLES `ClassificationAndLabelingVersions` WRITE;
/*!40000 ALTER TABLE `ClassificationAndLabelingVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ClassificationAndLabelingVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ClassificationAndLabelings`
--

DROP TABLE IF EXISTS `ClassificationAndLabelings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ClassificationAndLabelings` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CLPId` int(11) NOT NULL,
  `DPDId` int(11) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_ClassificationAndLabelings_CLPId` (`CLPId`),
  KEY `IX_ClassificationAndLabelings_DPDId` (`DPDId`),
  CONSTRAINT `FK_ClassificationAndLabelings_Clps_CLPId` FOREIGN KEY (`CLPId`) REFERENCES `Clps` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_ClassificationAndLabelings_Dpds_DPDId` FOREIGN KEY (`DPDId`) REFERENCES `Dpds` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ClassificationAndLabelings`
--

LOCK TABLES `ClassificationAndLabelings` WRITE;
/*!40000 ALTER TABLE `ClassificationAndLabelings` DISABLE KEYS */;
/*!40000 ALTER TABLE `ClassificationAndLabelings` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ClpVersions`
--

DROP TABLE IF EXISTS `ClpVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ClpVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CLPId` int(11) NOT NULL,
  `HazardPictograms` longtext DEFAULT NULL,
  `SignalWord` int(11) DEFAULT NULL,
  `BeeSymbol` int(11) DEFAULT NULL,
  `UFICode` longtext NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ClpVersions`
--

LOCK TABLES `ClpVersions` WRITE;
/*!40000 ALTER TABLE `ClpVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ClpVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Clps`
--

DROP TABLE IF EXISTS `Clps`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Clps` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `HazardPictograms` longtext DEFAULT NULL,
  `SignalWord` int(11) DEFAULT NULL,
  `BeeSymbol` int(11) DEFAULT NULL,
  `UFICode` longtext DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Clps`
--

LOCK TABLES `Clps` WRITE;
/*!40000 ALTER TABLE `Clps` DISABLE KEYS */;
/*!40000 ALTER TABLE `Clps` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `DpdVersions`
--

DROP TABLE IF EXISTS `DpdVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `DpdVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `DPDId` int(11) NOT NULL,
  `RiskPhrases` longtext NOT NULL,
  `IndicationDangerFlammable` int(11) DEFAULT NULL,
  `IndicationDangerToxicity` int(11) DEFAULT NULL,
  `IndicationDangerEnvironment` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `DpdVersions`
--

LOCK TABLES `DpdVersions` WRITE;
/*!40000 ALTER TABLE `DpdVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `DpdVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Dpds`
--

DROP TABLE IF EXISTS `Dpds`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Dpds` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `RiskPhrases` longtext NOT NULL,
  `IndicationDangerFlammable` int(11) DEFAULT NULL,
  `IndicationDangerToxicity` int(11) DEFAULT NULL,
  `IndicationDangerEnvironment` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Dpds`
--

LOCK TABLES `Dpds` WRITE;
/*!40000 ALTER TABLE `Dpds` DISABLE KEYS */;
/*!40000 ALTER TABLE `Dpds` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `HazardStatementVersions`
--

DROP TABLE IF EXISTS `HazardStatementVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `HazardStatementVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `HazardStatementId` int(11) NOT NULL,
  `Class` int(11) DEFAULT NULL,
  `Category` int(11) DEFAULT NULL,
  `Statement` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `HazardStatementVersions`
--

LOCK TABLES `HazardStatementVersions` WRITE;
/*!40000 ALTER TABLE `HazardStatementVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `HazardStatementVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `HazardStatements`
--

DROP TABLE IF EXISTS `HazardStatements`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `HazardStatements` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `CLPId` int(11) NOT NULL,
  `Class` int(11) DEFAULT NULL,
  `Category` int(11) DEFAULT NULL,
  `Statement` int(11) DEFAULT NULL,
  `CLPVersionId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_HazardStatements_CLPId` (`CLPId`),
  KEY `IX_HazardStatements_CLPVersionId` (`CLPVersionId`),
  CONSTRAINT `FK_HazardStatements_ClpVersions_CLPVersionId` FOREIGN KEY (`CLPVersionId`) REFERENCES `ClpVersions` (`Id`),
  CONSTRAINT `FK_HazardStatements_Clps_CLPId` FOREIGN KEY (`CLPId`) REFERENCES `Clps` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `HazardStatements`
--

LOCK TABLES `HazardStatements` WRITE;
/*!40000 ALTER TABLE `HazardStatements` DISABLE KEYS */;
/*!40000 ALTER TABLE `HazardStatements` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `LoadTaxVersions`
--

DROP TABLE IF EXISTS `LoadTaxVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `LoadTaxVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ProductId` int(11) NOT NULL,
  `LoadTaxId` int(11) NOT NULL,
  `Date` datetime(6) DEFAULT NULL,
  `Fee` double DEFAULT NULL,
  `EnvironmentalEffect` double DEFAULT NULL,
  `EnvironmentalBehaviour` double DEFAULT NULL,
  `Health` double DEFAULT NULL,
  `Concentration` double DEFAULT NULL,
  `Unit` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `LoadTaxVersions`
--

LOCK TABLES `LoadTaxVersions` WRITE;
/*!40000 ALTER TABLE `LoadTaxVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `LoadTaxVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `LoadTaxes`
--

DROP TABLE IF EXISTS `LoadTaxes`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `LoadTaxes` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ProductId` int(11) NOT NULL,
  `Date` datetime(6) DEFAULT NULL,
  `Fee` double DEFAULT NULL,
  `EnvironmentalEffect` double DEFAULT NULL,
  `EnvironmentalBehaviour` double DEFAULT NULL,
  `Health` double DEFAULT NULL,
  `Concentration` double DEFAULT NULL,
  `Unit` int(11) DEFAULT NULL,
  `ChemicalId` int(11) DEFAULT NULL,
  `ChemicalVersionId` int(11) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_LoadTaxes_ChemicalId` (`ChemicalId`),
  KEY `IX_LoadTaxes_ChemicalVersionId` (`ChemicalVersionId`),
  CONSTRAINT `FK_LoadTaxes_ChemicalVersions_ChemicalVersionId` FOREIGN KEY (`ChemicalVersionId`) REFERENCES `ChemicalVersions` (`Id`),
  CONSTRAINT `FK_LoadTaxes_Chemicals_ChemicalId` FOREIGN KEY (`ChemicalId`) REFERENCES `Chemicals` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `LoadTaxes`
--

LOCK TABLES `LoadTaxes` WRITE;
/*!40000 ALTER TABLE `LoadTaxes` DISABLE KEYS */;
/*!40000 ALTER TABLE `LoadTaxes` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `ProductVersions`
--

DROP TABLE IF EXISTS `ProductVersions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `ProductVersions` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalId` int(11) NOT NULL,
  `ProductId` int(11) NOT NULL,
  `Barcode` longtext DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
  `IsActive` tinyint(1) NOT NULL,
  `IsValid` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Checksum` longtext NOT NULL,
  `FileName` longtext NOT NULL,
  `Verified` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `ProductVersions`
--

LOCK TABLES `ProductVersions` WRITE;
/*!40000 ALTER TABLE `ProductVersions` DISABLE KEYS */;
/*!40000 ALTER TABLE `ProductVersions` ENABLE KEYS */;
UNLOCK TABLES;

--
-- Table structure for table `Products`
--

DROP TABLE IF EXISTS `Products`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8 */;
CREATE TABLE `Products` (
  `Id` int(11) NOT NULL AUTO_INCREMENT,
  `ChemicalId` int(11) NOT NULL,
  `Barcode` longtext DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
  `IsActive` tinyint(1) NOT NULL,
  `IsValid` tinyint(1) NOT NULL,
  `CreatedAt` datetime(6) NOT NULL,
  `UpdatedAt` datetime(6) NOT NULL,
  `WorkflowState` varchar(255) NOT NULL,
  `CreatedByUserId` int(11) NOT NULL,
  `UpdatedByUserId` int(11) NOT NULL,
  `Version` int(11) NOT NULL,
  `Checksum` longtext NOT NULL,
  `FileName` longtext NOT NULL,
  `Verified` tinyint(1) NOT NULL DEFAULT 0,
  PRIMARY KEY (`Id`),
  KEY `IX_Products_ChemicalId` (`ChemicalId`),
  CONSTRAINT `FK_Products_Chemicals_ChemicalId` FOREIGN KEY (`ChemicalId`) REFERENCES `Chemicals` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `Products`
--

LOCK TABLES `Products` WRITE;
/*!40000 ALTER TABLE `Products` DISABLE KEYS */;
/*!40000 ALTER TABLE `Products` ENABLE KEYS */;
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
INSERT INTO `__EFMigrationsHistory` VALUES ('20220529205849_InitialCreate','7.0.2'),('20220530092633_AddingAttributesToProduct','7.0.2'),('20220712145515_AddingVerifiedToChemicalAndProduct','7.0.2'),('20220811150444_AddingMoreAttributes','7.0.2'),('20220829190346_AddingBiocideProductGroupToChemical','7.0.2'),('20220830123251_SettingBiocideProductGroupBeNullable','7.0.2');
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

-- Dump completed on 2023-02-08 16:04:55
