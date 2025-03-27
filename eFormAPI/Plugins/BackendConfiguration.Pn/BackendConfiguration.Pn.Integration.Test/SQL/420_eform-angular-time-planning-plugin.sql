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
  `BreakFriday` int(11) DEFAULT NULL,
  `BreakMonday` int(11) DEFAULT NULL,
  `BreakSaturday` int(11) DEFAULT NULL,
  `BreakSunday` int(11) DEFAULT NULL,
  `BreakThursday` int(11) DEFAULT NULL,
  `BreakTuesday` int(11) DEFAULT NULL,
  `BreakWednesday` int(11) DEFAULT NULL,
  `EndFriday` int(11) DEFAULT NULL,
  `EndMonday` int(11) DEFAULT NULL,
  `EndSaturday` int(11) DEFAULT NULL,
  `EndSunday` int(11) DEFAULT NULL,
  `EndThursday` int(11) DEFAULT NULL,
  `EndTuesday` int(11) DEFAULT NULL,
  `EndWednesday` int(11) DEFAULT NULL,
  `StartFriday` int(11) DEFAULT NULL,
  `StartMonday` int(11) DEFAULT NULL,
  `StartSaturday` int(11) DEFAULT NULL,
  `StartSunday` int(11) DEFAULT NULL,
  `StartThursday` int(11) DEFAULT NULL,
  `StartTuesday` int(11) DEFAULT NULL,
  `StartWednesday` int(11) DEFAULT NULL,
  `Resigned` tinyint(1) NOT NULL DEFAULT 0,
  `AutoBreakCalculationActive` tinyint(1) NOT NULL DEFAULT 0,
  `FridayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `FridayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `FridayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `MondayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `MondayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `MondayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `SaturdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `SaturdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `SaturdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `SundayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `SundayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `SundayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `ThursdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `ThursdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `ThursdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `TuesdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `TuesdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `TuesdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `WednesdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `WednesdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `WednesdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `AllowAcceptOfPlannedHours` tinyint(1) NOT NULL DEFAULT 0,
  `AllowEditOfRegistrations` tinyint(1) NOT NULL DEFAULT 0,
  `AllowPersonalTimeRegistration` tinyint(1) NOT NULL DEFAULT 0,
  `BreakFriday2NdShift` int(11) DEFAULT NULL,
  `BreakFriday3RdShift` int(11) DEFAULT NULL,
  `BreakFriday4ThShift` int(11) DEFAULT NULL,
  `BreakFriday5ThShift` int(11) DEFAULT NULL,
  `BreakMonday2NdShift` int(11) DEFAULT NULL,
  `BreakMonday3RdShift` int(11) DEFAULT NULL,
  `BreakMonday4ThShift` int(11) DEFAULT NULL,
  `BreakMonday5ThShift` int(11) DEFAULT NULL,
  `BreakSaturday2NdShift` int(11) DEFAULT NULL,
  `BreakSaturday3RdShift` int(11) DEFAULT NULL,
  `BreakSaturday4ThShift` int(11) DEFAULT NULL,
  `BreakSaturday5ThShift` int(11) DEFAULT NULL,
  `BreakSunday2NdShift` int(11) DEFAULT NULL,
  `BreakSunday3RdShift` int(11) DEFAULT NULL,
  `BreakSunday4ThShift` int(11) DEFAULT NULL,
  `BreakSunday5ThShift` int(11) DEFAULT NULL,
  `BreakThursday2NdShift` int(11) DEFAULT NULL,
  `BreakThursday3RdShift` int(11) DEFAULT NULL,
  `BreakThursday4ThShift` int(11) DEFAULT NULL,
  `BreakThursday5ThShift` int(11) DEFAULT NULL,
  `BreakTuesday2NdShift` int(11) DEFAULT NULL,
  `BreakTuesday3RdShift` int(11) DEFAULT NULL,
  `BreakTuesday4ThShift` int(11) DEFAULT NULL,
  `BreakTuesday5ThShift` int(11) DEFAULT NULL,
  `BreakWednesday2NdShift` int(11) DEFAULT NULL,
  `BreakWednesday3RdShift` int(11) DEFAULT NULL,
  `BreakWednesday4ThShift` int(11) DEFAULT NULL,
  `BreakWednesday5ThShift` int(11) DEFAULT NULL,
  `EndFriday2NdShift` int(11) DEFAULT NULL,
  `EndFriday3RdShift` int(11) DEFAULT NULL,
  `EndFriday4ThShift` int(11) DEFAULT NULL,
  `EndFriday5ThShift` int(11) DEFAULT NULL,
  `EndMonday2NdShift` int(11) DEFAULT NULL,
  `EndMonday3RdShift` int(11) DEFAULT NULL,
  `EndMonday4ThShift` int(11) DEFAULT NULL,
  `EndMonday5ThShift` int(11) DEFAULT NULL,
  `EndSaturday2NdShift` int(11) DEFAULT NULL,
  `EndSaturday3RdShift` int(11) DEFAULT NULL,
  `EndSaturday4ThShift` int(11) DEFAULT NULL,
  `EndSaturday5ThShift` int(11) DEFAULT NULL,
  `EndSunday2NdShift` int(11) DEFAULT NULL,
  `EndSunday3RdShift` int(11) DEFAULT NULL,
  `EndSunday4ThShift` int(11) DEFAULT NULL,
  `EndSunday5ThShift` int(11) DEFAULT NULL,
  `EndThursday2NdShift` int(11) DEFAULT NULL,
  `EndThursday3RdShift` int(11) DEFAULT NULL,
  `EndThursday4ThShift` int(11) DEFAULT NULL,
  `EndThursday5ThShift` int(11) DEFAULT NULL,
  `EndTuesday2NdShift` int(11) DEFAULT NULL,
  `EndTuesday3RdShift` int(11) DEFAULT NULL,
  `EndTuesday4ThShift` int(11) DEFAULT NULL,
  `EndTuesday5ThShift` int(11) DEFAULT NULL,
  `EndWednesday2NdShift` int(11) DEFAULT NULL,
  `EndWednesday3RdShift` int(11) DEFAULT NULL,
  `EndWednesday4ThShift` int(11) DEFAULT NULL,
  `EndWednesday5ThShift` int(11) DEFAULT NULL,
  `StartFriday2NdShift` int(11) DEFAULT NULL,
  `StartFriday3RdShift` int(11) DEFAULT NULL,
  `StartFriday4ThShift` int(11) DEFAULT NULL,
  `StartFriday5ThShift` int(11) DEFAULT NULL,
  `StartMonday2NdShift` int(11) DEFAULT NULL,
  `StartMonday3RdShift` int(11) DEFAULT NULL,
  `StartMonday4ThShift` int(11) DEFAULT NULL,
  `StartMonday5ThShift` int(11) DEFAULT NULL,
  `StartSaturday2NdShift` int(11) DEFAULT NULL,
  `StartSaturday3RdShift` int(11) DEFAULT NULL,
  `StartSaturday4ThShift` int(11) DEFAULT NULL,
  `StartSaturday5ThShift` int(11) DEFAULT NULL,
  `StartSunday2NdShift` int(11) DEFAULT NULL,
  `StartSunday3RdShift` int(11) DEFAULT NULL,
  `StartSunday4ThShift` int(11) DEFAULT NULL,
  `StartSunday5ThShift` int(11) DEFAULT NULL,
  `StartThursday2NdShift` int(11) DEFAULT NULL,
  `StartThursday3RdShift` int(11) DEFAULT NULL,
  `StartThursday4ThShift` int(11) DEFAULT NULL,
  `StartThursday5ThShift` int(11) DEFAULT NULL,
  `StartTuesday2NdShift` int(11) DEFAULT NULL,
  `StartTuesday3RdShift` int(11) DEFAULT NULL,
  `StartTuesday4ThShift` int(11) DEFAULT NULL,
  `StartTuesday5ThShift` int(11) DEFAULT NULL,
  `StartWednesday2NdShift` int(11) DEFAULT NULL,
  `StartWednesday3RdShift` int(11) DEFAULT NULL,
  `StartWednesday4ThShift` int(11) DEFAULT NULL,
  `StartWednesday5ThShift` int(11) DEFAULT NULL,
  `UseOneMinuteIntervals` tinyint(1) NOT NULL DEFAULT 0,
  `UseGoogleSheetAsDefault` tinyint(1) NOT NULL DEFAULT 0,
  `UseOnlyPlanHours` tinyint(1) NOT NULL DEFAULT 0,
  `FridayPlanHours` int(11) NOT NULL DEFAULT 0,
  `MondayPlanHours` int(11) NOT NULL DEFAULT 0,
  `SaturdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `SundayPlanHours` int(11) NOT NULL DEFAULT 0,
  `ThursdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `TuesdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `WednesdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `UsePunchClock` tinyint(1) NOT NULL DEFAULT 0,
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
  `BreakFriday` int(11) DEFAULT NULL,
  `BreakMonday` int(11) DEFAULT NULL,
  `BreakSaturday` int(11) DEFAULT NULL,
  `BreakSunday` int(11) DEFAULT NULL,
  `BreakThursday` int(11) DEFAULT NULL,
  `BreakTuesday` int(11) DEFAULT NULL,
  `BreakWednesday` int(11) DEFAULT NULL,
  `EndFriday` int(11) DEFAULT NULL,
  `EndMonday` int(11) DEFAULT NULL,
  `EndSaturday` int(11) DEFAULT NULL,
  `EndSunday` int(11) DEFAULT NULL,
  `EndThursday` int(11) DEFAULT NULL,
  `EndTuesday` int(11) DEFAULT NULL,
  `EndWednesday` int(11) DEFAULT NULL,
  `StartFriday` int(11) DEFAULT NULL,
  `StartMonday` int(11) DEFAULT NULL,
  `StartSaturday` int(11) DEFAULT NULL,
  `StartSunday` int(11) DEFAULT NULL,
  `StartThursday` int(11) DEFAULT NULL,
  `StartTuesday` int(11) DEFAULT NULL,
  `StartWednesday` int(11) DEFAULT NULL,
  `Resigned` tinyint(1) NOT NULL DEFAULT 0,
  `AutoBreakCalculationActive` tinyint(1) NOT NULL DEFAULT 0,
  `FridayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `FridayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `FridayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `MondayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `MondayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `MondayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `SaturdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `SaturdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `SaturdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `SundayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `SundayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `SundayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `ThursdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `ThursdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `ThursdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `TuesdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `TuesdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `TuesdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `WednesdayBreakMinutesDivider` int(11) NOT NULL DEFAULT 0,
  `WednesdayBreakMinutesPrDivider` int(11) NOT NULL DEFAULT 0,
  `WednesdayBreakMinutesUpperLimit` int(11) NOT NULL DEFAULT 0,
  `AllowAcceptOfPlannedHours` tinyint(1) NOT NULL DEFAULT 0,
  `AllowEditOfRegistrations` tinyint(1) NOT NULL DEFAULT 0,
  `AllowPersonalTimeRegistration` tinyint(1) NOT NULL DEFAULT 0,
  `BreakFriday2NdShift` int(11) DEFAULT NULL,
  `BreakFriday3RdShift` int(11) DEFAULT NULL,
  `BreakFriday4ThShift` int(11) DEFAULT NULL,
  `BreakFriday5ThShift` int(11) DEFAULT NULL,
  `BreakMonday2NdShift` int(11) DEFAULT NULL,
  `BreakMonday3RdShift` int(11) DEFAULT NULL,
  `BreakMonday4ThShift` int(11) DEFAULT NULL,
  `BreakMonday5ThShift` int(11) DEFAULT NULL,
  `BreakSaturday2NdShift` int(11) DEFAULT NULL,
  `BreakSaturday3RdShift` int(11) DEFAULT NULL,
  `BreakSaturday4ThShift` int(11) DEFAULT NULL,
  `BreakSaturday5ThShift` int(11) DEFAULT NULL,
  `BreakSunday2NdShift` int(11) DEFAULT NULL,
  `BreakSunday3RdShift` int(11) DEFAULT NULL,
  `BreakSunday4ThShift` int(11) DEFAULT NULL,
  `BreakSunday5ThShift` int(11) DEFAULT NULL,
  `BreakThursday2NdShift` int(11) DEFAULT NULL,
  `BreakThursday3RdShift` int(11) DEFAULT NULL,
  `BreakThursday4ThShift` int(11) DEFAULT NULL,
  `BreakThursday5ThShift` int(11) DEFAULT NULL,
  `BreakTuesday2NdShift` int(11) DEFAULT NULL,
  `BreakTuesday3RdShift` int(11) DEFAULT NULL,
  `BreakTuesday4ThShift` int(11) DEFAULT NULL,
  `BreakTuesday5ThShift` int(11) DEFAULT NULL,
  `BreakWednesday2NdShift` int(11) DEFAULT NULL,
  `BreakWednesday3RdShift` int(11) DEFAULT NULL,
  `BreakWednesday4ThShift` int(11) DEFAULT NULL,
  `BreakWednesday5ThShift` int(11) DEFAULT NULL,
  `EndFriday2NdShift` int(11) DEFAULT NULL,
  `EndFriday3RdShift` int(11) DEFAULT NULL,
  `EndFriday4ThShift` int(11) DEFAULT NULL,
  `EndFriday5ThShift` int(11) DEFAULT NULL,
  `EndMonday2NdShift` int(11) DEFAULT NULL,
  `EndMonday3RdShift` int(11) DEFAULT NULL,
  `EndMonday4ThShift` int(11) DEFAULT NULL,
  `EndMonday5ThShift` int(11) DEFAULT NULL,
  `EndSaturday2NdShift` int(11) DEFAULT NULL,
  `EndSaturday3RdShift` int(11) DEFAULT NULL,
  `EndSaturday4ThShift` int(11) DEFAULT NULL,
  `EndSaturday5ThShift` int(11) DEFAULT NULL,
  `EndSunday2NdShift` int(11) DEFAULT NULL,
  `EndSunday3RdShift` int(11) DEFAULT NULL,
  `EndSunday4ThShift` int(11) DEFAULT NULL,
  `EndSunday5ThShift` int(11) DEFAULT NULL,
  `EndThursday2NdShift` int(11) DEFAULT NULL,
  `EndThursday3RdShift` int(11) DEFAULT NULL,
  `EndThursday4ThShift` int(11) DEFAULT NULL,
  `EndThursday5ThShift` int(11) DEFAULT NULL,
  `EndTuesday2NdShift` int(11) DEFAULT NULL,
  `EndTuesday3RdShift` int(11) DEFAULT NULL,
  `EndTuesday4ThShift` int(11) DEFAULT NULL,
  `EndTuesday5ThShift` int(11) DEFAULT NULL,
  `EndWednesday2NdShift` int(11) DEFAULT NULL,
  `EndWednesday3RdShift` int(11) DEFAULT NULL,
  `EndWednesday4ThShift` int(11) DEFAULT NULL,
  `EndWednesday5ThShift` int(11) DEFAULT NULL,
  `StartFriday2NdShift` int(11) DEFAULT NULL,
  `StartFriday3RdShift` int(11) DEFAULT NULL,
  `StartFriday4ThShift` int(11) DEFAULT NULL,
  `StartFriday5ThShift` int(11) DEFAULT NULL,
  `StartMonday2NdShift` int(11) DEFAULT NULL,
  `StartMonday3RdShift` int(11) DEFAULT NULL,
  `StartMonday4ThShift` int(11) DEFAULT NULL,
  `StartMonday5ThShift` int(11) DEFAULT NULL,
  `StartSaturday2NdShift` int(11) DEFAULT NULL,
  `StartSaturday3RdShift` int(11) DEFAULT NULL,
  `StartSaturday4ThShift` int(11) DEFAULT NULL,
  `StartSaturday5ThShift` int(11) DEFAULT NULL,
  `StartSunday2NdShift` int(11) DEFAULT NULL,
  `StartSunday3RdShift` int(11) DEFAULT NULL,
  `StartSunday4ThShift` int(11) DEFAULT NULL,
  `StartSunday5ThShift` int(11) DEFAULT NULL,
  `StartThursday2NdShift` int(11) DEFAULT NULL,
  `StartThursday3RdShift` int(11) DEFAULT NULL,
  `StartThursday4ThShift` int(11) DEFAULT NULL,
  `StartThursday5ThShift` int(11) DEFAULT NULL,
  `StartTuesday2NdShift` int(11) DEFAULT NULL,
  `StartTuesday3RdShift` int(11) DEFAULT NULL,
  `StartTuesday4ThShift` int(11) DEFAULT NULL,
  `StartTuesday5ThShift` int(11) DEFAULT NULL,
  `StartWednesday2NdShift` int(11) DEFAULT NULL,
  `StartWednesday3RdShift` int(11) DEFAULT NULL,
  `StartWednesday4ThShift` int(11) DEFAULT NULL,
  `StartWednesday5ThShift` int(11) DEFAULT NULL,
  `UseOneMinuteIntervals` tinyint(1) NOT NULL DEFAULT 0,
  `UseGoogleSheetAsDefault` tinyint(1) NOT NULL DEFAULT 0,
  `UseOnlyPlanHours` tinyint(1) NOT NULL DEFAULT 0,
  `FridayPlanHours` int(11) NOT NULL DEFAULT 0,
  `MondayPlanHours` int(11) NOT NULL DEFAULT 0,
  `SaturdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `SundayPlanHours` int(11) NOT NULL DEFAULT 0,
  `ThursdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `TuesdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `WednesdayPlanHours` int(11) NOT NULL DEFAULT 0,
  `UsePunchClock` tinyint(1) NOT NULL DEFAULT 0,
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
  `Pause100StartedAt` datetime(6) DEFAULT NULL,
  `Pause100StoppedAt` datetime(6) DEFAULT NULL,
  `Pause101StartedAt` datetime(6) DEFAULT NULL,
  `Pause101StoppedAt` datetime(6) DEFAULT NULL,
  `Pause102StartedAt` datetime(6) DEFAULT NULL,
  `Pause102StoppedAt` datetime(6) DEFAULT NULL,
  `Pause10StartedAt` datetime(6) DEFAULT NULL,
  `Pause10StoppedAt` datetime(6) DEFAULT NULL,
  `Pause11StartedAt` datetime(6) DEFAULT NULL,
  `Pause11StoppedAt` datetime(6) DEFAULT NULL,
  `Pause12StartedAt` datetime(6) DEFAULT NULL,
  `Pause12StoppedAt` datetime(6) DEFAULT NULL,
  `Pause13StartedAt` datetime(6) DEFAULT NULL,
  `Pause13StoppedAt` datetime(6) DEFAULT NULL,
  `Pause14StartedAt` datetime(6) DEFAULT NULL,
  `Pause14StoppedAt` datetime(6) DEFAULT NULL,
  `Pause15StartedAt` datetime(6) DEFAULT NULL,
  `Pause15StoppedAt` datetime(6) DEFAULT NULL,
  `Pause16StartedAt` datetime(6) DEFAULT NULL,
  `Pause16StoppedAt` datetime(6) DEFAULT NULL,
  `Pause17StartedAt` datetime(6) DEFAULT NULL,
  `Pause17StoppedAt` datetime(6) DEFAULT NULL,
  `Pause18StartedAt` datetime(6) DEFAULT NULL,
  `Pause18StoppedAt` datetime(6) DEFAULT NULL,
  `Pause19StartedAt` datetime(6) DEFAULT NULL,
  `Pause19StoppedAt` datetime(6) DEFAULT NULL,
  `Pause200StartedAt` datetime(6) DEFAULT NULL,
  `Pause200StoppedAt` datetime(6) DEFAULT NULL,
  `Pause201StartedAt` datetime(6) DEFAULT NULL,
  `Pause201StoppedAt` datetime(6) DEFAULT NULL,
  `Pause202StartedAt` datetime(6) DEFAULT NULL,
  `Pause202StoppedAt` datetime(6) DEFAULT NULL,
  `Pause20StartedAt` datetime(6) DEFAULT NULL,
  `Pause20StoppedAt` datetime(6) DEFAULT NULL,
  `Pause21StartedAt` datetime(6) DEFAULT NULL,
  `Pause21StoppedAt` datetime(6) DEFAULT NULL,
  `Pause22StartedAt` datetime(6) DEFAULT NULL,
  `Pause22StoppedAt` datetime(6) DEFAULT NULL,
  `Pause23StartedAt` datetime(6) DEFAULT NULL,
  `Pause23StoppedAt` datetime(6) DEFAULT NULL,
  `Pause24StartedAt` datetime(6) DEFAULT NULL,
  `Pause24StoppedAt` datetime(6) DEFAULT NULL,
  `Pause25StartedAt` datetime(6) DEFAULT NULL,
  `Pause25StoppedAt` datetime(6) DEFAULT NULL,
  `Pause26StartedAt` datetime(6) DEFAULT NULL,
  `Pause26StoppedAt` datetime(6) DEFAULT NULL,
  `Pause27StartedAt` datetime(6) DEFAULT NULL,
  `Pause27StoppedAt` datetime(6) DEFAULT NULL,
  `Pause28StartedAt` datetime(6) DEFAULT NULL,
  `Pause28StoppedAt` datetime(6) DEFAULT NULL,
  `Pause29StartedAt` datetime(6) DEFAULT NULL,
  `Pause29StoppedAt` datetime(6) DEFAULT NULL,
  `Shift1PauseNumber` int(11) NOT NULL DEFAULT 0,
  `Shift2PauseNumber` int(11) NOT NULL DEFAULT 0,
  `IsDoubleShift` tinyint(1) NOT NULL DEFAULT 0,
  `PlannedBreakOfShift1` int(11) NOT NULL DEFAULT 0,
  `PlannedBreakOfShift2` int(11) NOT NULL DEFAULT 0,
  `PlannedEndOfShift1` int(11) NOT NULL DEFAULT 0,
  `PlannedEndOfShift2` int(11) NOT NULL DEFAULT 0,
  `PlannedStartOfShift1` int(11) NOT NULL DEFAULT 0,
  `PlannedStartOfShift2` int(11) NOT NULL DEFAULT 0,
  `AbsenceWithoutPermission` tinyint(1) NOT NULL DEFAULT 0,
  `OnVacation` tinyint(1) NOT NULL DEFAULT 0,
  `OtherAllowedAbsence` tinyint(1) NOT NULL DEFAULT 0,
  `Sick` tinyint(1) NOT NULL DEFAULT 0,
  `Pause3Id` int(11) NOT NULL DEFAULT 0,
  `Pause3StartedAt` datetime(6) DEFAULT NULL,
  `Pause3StoppedAt` datetime(6) DEFAULT NULL,
  `Pause4Id` int(11) NOT NULL DEFAULT 0,
  `Pause4StartedAt` datetime(6) DEFAULT NULL,
  `Pause4StoppedAt` datetime(6) DEFAULT NULL,
  `Pause5Id` int(11) NOT NULL DEFAULT 0,
  `Pause5StartedAt` datetime(6) DEFAULT NULL,
  `Pause5StoppedAt` datetime(6) DEFAULT NULL,
  `Start3Id` int(11) NOT NULL DEFAULT 0,
  `Start3StartedAt` datetime(6) DEFAULT NULL,
  `Start4Id` int(11) NOT NULL DEFAULT 0,
  `Start4StartedAt` datetime(6) DEFAULT NULL,
  `Start5Id` int(11) NOT NULL DEFAULT 0,
  `Start5StartedAt` datetime(6) DEFAULT NULL,
  `Stop3Id` int(11) NOT NULL DEFAULT 0,
  `Stop3StoppedAt` datetime(6) DEFAULT NULL,
  `Stop4Id` int(11) NOT NULL DEFAULT 0,
  `Stop4StoppedAt` datetime(6) DEFAULT NULL,
  `Stop5Id` int(11) NOT NULL DEFAULT 0,
  `Stop5StoppedAt` datetime(6) DEFAULT NULL,
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
  `Pause100StartedAt` datetime(6) DEFAULT NULL,
  `Pause100StoppedAt` datetime(6) DEFAULT NULL,
  `Pause101StartedAt` datetime(6) DEFAULT NULL,
  `Pause101StoppedAt` datetime(6) DEFAULT NULL,
  `Pause102StartedAt` datetime(6) DEFAULT NULL,
  `Pause102StoppedAt` datetime(6) DEFAULT NULL,
  `Pause10StartedAt` datetime(6) DEFAULT NULL,
  `Pause10StoppedAt` datetime(6) DEFAULT NULL,
  `Pause11StartedAt` datetime(6) DEFAULT NULL,
  `Pause11StoppedAt` datetime(6) DEFAULT NULL,
  `Pause12StartedAt` datetime(6) DEFAULT NULL,
  `Pause12StoppedAt` datetime(6) DEFAULT NULL,
  `Pause13StartedAt` datetime(6) DEFAULT NULL,
  `Pause13StoppedAt` datetime(6) DEFAULT NULL,
  `Pause14StartedAt` datetime(6) DEFAULT NULL,
  `Pause14StoppedAt` datetime(6) DEFAULT NULL,
  `Pause15StartedAt` datetime(6) DEFAULT NULL,
  `Pause15StoppedAt` datetime(6) DEFAULT NULL,
  `Pause16StartedAt` datetime(6) DEFAULT NULL,
  `Pause16StoppedAt` datetime(6) DEFAULT NULL,
  `Pause17StartedAt` datetime(6) DEFAULT NULL,
  `Pause17StoppedAt` datetime(6) DEFAULT NULL,
  `Pause18StartedAt` datetime(6) DEFAULT NULL,
  `Pause18StoppedAt` datetime(6) DEFAULT NULL,
  `Pause19StartedAt` datetime(6) DEFAULT NULL,
  `Pause19StoppedAt` datetime(6) DEFAULT NULL,
  `Pause200StartedAt` datetime(6) DEFAULT NULL,
  `Pause200StoppedAt` datetime(6) DEFAULT NULL,
  `Pause201StartedAt` datetime(6) DEFAULT NULL,
  `Pause201StoppedAt` datetime(6) DEFAULT NULL,
  `Pause202StartedAt` datetime(6) DEFAULT NULL,
  `Pause202StoppedAt` datetime(6) DEFAULT NULL,
  `Pause20StartedAt` datetime(6) DEFAULT NULL,
  `Pause20StoppedAt` datetime(6) DEFAULT NULL,
  `Pause21StartedAt` datetime(6) DEFAULT NULL,
  `Pause21StoppedAt` datetime(6) DEFAULT NULL,
  `Pause22StartedAt` datetime(6) DEFAULT NULL,
  `Pause22StoppedAt` datetime(6) DEFAULT NULL,
  `Pause23StartedAt` datetime(6) DEFAULT NULL,
  `Pause23StoppedAt` datetime(6) DEFAULT NULL,
  `Pause24StartedAt` datetime(6) DEFAULT NULL,
  `Pause24StoppedAt` datetime(6) DEFAULT NULL,
  `Pause25StartedAt` datetime(6) DEFAULT NULL,
  `Pause25StoppedAt` datetime(6) DEFAULT NULL,
  `Pause26StartedAt` datetime(6) DEFAULT NULL,
  `Pause26StoppedAt` datetime(6) DEFAULT NULL,
  `Pause27StartedAt` datetime(6) DEFAULT NULL,
  `Pause27StoppedAt` datetime(6) DEFAULT NULL,
  `Pause28StartedAt` datetime(6) DEFAULT NULL,
  `Pause28StoppedAt` datetime(6) DEFAULT NULL,
  `Pause29StartedAt` datetime(6) DEFAULT NULL,
  `Pause29StoppedAt` datetime(6) DEFAULT NULL,
  `Shift1PauseNumber` int(11) NOT NULL DEFAULT 0,
  `Shift2PauseNumber` int(11) NOT NULL DEFAULT 0,
  `IsDoubleShift` tinyint(1) NOT NULL DEFAULT 0,
  `PlannedBreakOfShift1` int(11) NOT NULL DEFAULT 0,
  `PlannedBreakOfShift2` int(11) NOT NULL DEFAULT 0,
  `PlannedEndOfShift1` int(11) NOT NULL DEFAULT 0,
  `PlannedEndOfShift2` int(11) NOT NULL DEFAULT 0,
  `PlannedStartOfShift1` int(11) NOT NULL DEFAULT 0,
  `PlannedStartOfShift2` int(11) NOT NULL DEFAULT 0,
  `AbsenceWithoutPermission` tinyint(1) NOT NULL DEFAULT 0,
  `OnVacation` tinyint(1) NOT NULL DEFAULT 0,
  `OtherAllowedAbsence` tinyint(1) NOT NULL DEFAULT 0,
  `Sick` tinyint(1) NOT NULL DEFAULT 0,
  `Pause3Id` int(11) NOT NULL DEFAULT 0,
  `Pause3StartedAt` datetime(6) DEFAULT NULL,
  `Pause3StoppedAt` datetime(6) DEFAULT NULL,
  `Pause4Id` int(11) NOT NULL DEFAULT 0,
  `Pause4StartedAt` datetime(6) DEFAULT NULL,
  `Pause4StoppedAt` datetime(6) DEFAULT NULL,
  `Pause5Id` int(11) NOT NULL DEFAULT 0,
  `Pause5StartedAt` datetime(6) DEFAULT NULL,
  `Pause5StoppedAt` datetime(6) DEFAULT NULL,
  `Start3Id` int(11) NOT NULL DEFAULT 0,
  `Start3StartedAt` datetime(6) DEFAULT NULL,
  `Start4Id` int(11) NOT NULL DEFAULT 0,
  `Start4StartedAt` datetime(6) DEFAULT NULL,
  `Start5Id` int(11) NOT NULL DEFAULT 0,
  `Start5StartedAt` datetime(6) DEFAULT NULL,
  `Stop3Id` int(11) NOT NULL DEFAULT 0,
  `Stop3StoppedAt` datetime(6) DEFAULT NULL,
  `Stop4Id` int(11) NOT NULL DEFAULT 0,
  `Stop4StoppedAt` datetime(6) DEFAULT NULL,
  `Stop5Id` int(11) NOT NULL DEFAULT 0,
  `Stop5StoppedAt` datetime(6) DEFAULT NULL,
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
  `Description` longtext DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
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
  `Description` longtext DEFAULT NULL,
  `Name` longtext DEFAULT NULL,
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
