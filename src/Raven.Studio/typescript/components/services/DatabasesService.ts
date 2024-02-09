/// <reference path="../../../typings/tsd.d.ts" />

import saveDatabaseLockModeCommand from "commands/resources/saveDatabaseLockModeCommand";
import DatabaseLockMode = Raven.Client.ServerWide.DatabaseLockMode;
import { DatabaseSharedInfo } from "../models/databases";
import database from "models/resources/database";
import EssentialDatabaseStatistics = Raven.Client.Documents.Operations.EssentialDatabaseStatistics;
import getEssentialDatabaseStatsCommand from "commands/resources/getEssentialDatabaseStatsCommand";
import DetailedDatabaseStatistics = Raven.Client.Documents.Operations.DetailedDatabaseStatistics;
import getDatabaseDetailedStatsCommand from "commands/resources/getDatabaseDetailedStatsCommand";
import deleteDatabaseFromNodeCommand from "commands/resources/deleteDatabaseFromNodeCommand";
import toggleDynamicNodeAssignmentCommand from "commands/database/dbGroup/toggleDynamicNodeAssignmentCommand";
import reorderNodesInDatabaseGroupCommand = require("commands/database/dbGroup/reorderNodesInDatabaseGroupCommand");
import deleteOrchestratorFromNodeCommand from "commands/resources/deleteOrchestratorFromNodeCommand";
import deleteDatabaseCommand from "commands/resources/deleteDatabaseCommand";
import toggleDatabaseCommand from "commands/resources/toggleDatabaseCommand";
import StudioDatabasesState = Raven.Server.Web.System.Processors.Studio.StudioDatabasesHandlerForGetDatabasesState.StudioDatabasesState;
import getDatabasesStateForStudioCommand from "commands/resources/getDatabasesStateForStudioCommand";
import getDatabaseStateForStudioCommand from "commands/resources/getDatabaseStateForStudioCommand";
import restartDatabaseCommand = require("commands/resources/restartDatabaseCommand");
import getDatabaseStudioConfigurationCommand = require("commands/resources/getDatabaseStudioConfigurationCommand");
import StudioConfiguration = Raven.Client.Documents.Operations.Configuration.StudioConfiguration;
import saveDatabaseStudioConfigurationCommand = require("commands/resources/saveDatabaseStudioConfigurationCommand");
import getNextOperationIdCommand = require("commands/database/studio/getNextOperationIdCommand");
import killOperationCommand = require("commands/operations/killOperationCommand");
import getRefreshConfigurationCommand = require("commands/database/documents/getRefreshConfigurationCommand");
import saveRefreshConfigurationCommand = require("commands/database/documents/saveRefreshConfigurationCommand");
import RefreshConfiguration = Raven.Client.Documents.Operations.Refresh.RefreshConfiguration;
import ExpirationConfiguration = Raven.Client.Documents.Operations.Expiration.ExpirationConfiguration;
import saveExpirationConfigurationCommand = require("commands/database/documents/saveExpirationConfigurationCommand");
import getExpirationConfigurationCommand = require("commands/database/documents/getExpirationConfigurationCommand");
import DataArchivalConfiguration = Raven.Client.Documents.Operations.DataArchival.DataArchivalConfiguration;
import getDataArchivalConfigurationCommand from "commands/database/documents/getDataArchivalConfigurationCommand";
import saveDataArchivalConfigurationCommand from "commands/database/documents/saveDataArchivalConfigurationCommand";
import getTombstonesStateCommand = require("commands/database/debug/getTombstonesStateCommand");
import forceTombstonesCleanupCommand = require("commands/database/debug/forceTombstonesCleanupCommand");
import RevisionsConfiguration = Raven.Client.Documents.Operations.Revisions.RevisionsConfiguration;
import RevisionsCollectionConfiguration = Raven.Client.Documents.Operations.Revisions.RevisionsCollectionConfiguration;
import getRevisionsForConflictsConfigurationCommand = require("commands/database/documents/getRevisionsForConflictsConfigurationCommand");
import getRevisionsConfigurationCommand = require("commands/database/documents/getRevisionsConfigurationCommand");
import saveRevisionsConfigurationCommand = require("commands/database/documents/saveRevisionsConfigurationCommand");
import saveRevisionsForConflictsConfigurationCommand = require("commands/database/documents/saveRevisionsForConflictsConfigurationCommand");
import enforceRevisionsConfigurationCommand = require("commands/database/settings/enforceRevisionsConfigurationCommand");
import getCustomSortersCommand = require("commands/database/settings/getCustomSortersCommand");
import deleteCustomSorterCommand = require("commands/database/settings/deleteCustomSorterCommand");
import deleteCustomAnalyzerCommand = require("commands/database/settings/deleteCustomAnalyzerCommand");
import getCustomAnalyzersCommand = require("commands/database/settings/getCustomAnalyzersCommand");
import getDocumentsCompressionConfigurationCommand = require("commands/database/documents/getDocumentsCompressionConfigurationCommand");
import saveDocumentsCompressionCommand = require("commands/database/documents/saveDocumentsCompressionCommand");
import promoteDatabaseNodeCommand = require("commands/database/debug/promoteDatabaseNodeCommand");
import saveConnectionStringCommand = require("commands/database/settings/saveConnectionStringCommand");
import { ConnectionStringDto } from "components/pages/database/settings/connectionStrings/connectionStringsTypes";
import testClusterNodeConnectionCommand = require("commands/database/cluster/testClusterNodeConnectionCommand");
import getConnectionStringsCommand = require("commands/database/settings/getConnectionStringsCommand");
import deleteConnectionStringCommand = require("commands/database/settings/deleteConnectionStringCommand");
import revertRevisionsCommand = require("commands/database/documents/revertRevisionsCommand");
import getConflictSolverConfigurationCommand = require("commands/database/documents/getConflictSolverConfigurationCommand");
import testSqlConnectionStringCommand = require("commands/database/cluster/testSqlConnectionStringCommand");
import testRabbitMqServerConnectionCommand = require("commands/database/cluster/testRabbitMqServerConnectionCommand");
import getDatabaseRecordCommand = require("commands/resources/getDatabaseRecordCommand");
import saveDatabaseRecordCommand = require("commands/resources/saveDatabaseRecordCommand");
import saveConflictSolverConfigurationCommand = require("commands/database/documents/saveConflictSolverConfigurationCommand");

export default class DatabasesService {
    async setLockMode(databases: DatabaseSharedInfo[], newLockMode: DatabaseLockMode) {
        return new saveDatabaseLockModeCommand(databases, newLockMode).execute();
    }

    async toggle(databases: DatabaseSharedInfo[], enable: boolean) {
        return new toggleDatabaseCommand(databases, enable).execute();
    }

    async deleteDatabase(toDelete: string[], hardDelete: boolean) {
        return new deleteDatabaseCommand(toDelete, hardDelete).execute();
    }

    async getEssentialStats(db: DatabaseSharedInfo): Promise<EssentialDatabaseStatistics> {
        return new getEssentialDatabaseStatsCommand(db).execute();
    }

    async getDatabasesState(targetNodeTag: string): Promise<StudioDatabasesState> {
        return new getDatabasesStateForStudioCommand(targetNodeTag).execute();
    }

    async getDatabaseState(targetNodeTag: string, databaseName: string): Promise<StudioDatabasesState> {
        return new getDatabaseStateForStudioCommand(targetNodeTag, databaseName).execute();
    }

    async getDetailedStats(
        db: DatabaseSharedInfo,
        location: databaseLocationSpecifier
    ): Promise<DetailedDatabaseStatistics> {
        return new getDatabaseDetailedStatsCommand(db, location).execute();
    }

    async deleteDatabaseFromNode(db: DatabaseSharedInfo, nodes: string[], hardDelete: boolean) {
        return new deleteDatabaseFromNodeCommand(db, nodes, hardDelete).execute();
    }

    async deleteOrchestratorFromNode(db: DatabaseSharedInfo, node: string) {
        return new deleteOrchestratorFromNodeCommand(db, node).execute();
    }

    async toggleDynamicNodeAssignment(databaseName: string, enabled: boolean) {
        return new toggleDynamicNodeAssignmentCommand(databaseName, enabled).execute();
    }

    async reorderNodesInGroup(db: DatabaseSharedInfo, tagsOrder: string[], fixOrder: boolean) {
        return new reorderNodesInDatabaseGroupCommand(db.name, tagsOrder, fixOrder).execute();
    }

    async restartDatabase(db: DatabaseSharedInfo, location: databaseLocationSpecifier) {
        return new restartDatabaseCommand(db, location).execute();
    }

    async getDatabaseStudioConfiguration(db: database) {
        return new getDatabaseStudioConfigurationCommand(db).execute();
    }

    async saveDatabaseStudioConfiguration(dto: Partial<StudioConfiguration>, db: database) {
        return new saveDatabaseStudioConfigurationCommand(dto, db).execute();
    }

    async getNextOperationId(db: database) {
        return new getNextOperationIdCommand(db).execute();
    }

    async killOperation(db: database, taskId: number) {
        return new killOperationCommand(db, taskId).execute();
    }

    async getRefreshConfiguration(db: database) {
        return new getRefreshConfigurationCommand(db).execute();
    }

    async saveRefreshConfiguration(db: database, dto: RefreshConfiguration) {
        return new saveRefreshConfigurationCommand(db, dto).execute();
    }

    async getDataArchivalConfiguration(
        db: database
    ): Promise<Raven.Client.Documents.Operations.DataArchival.DataArchivalConfiguration> {
        return new getDataArchivalConfigurationCommand(db).execute();
    }

    async saveDataArchivalConfiguration(db: database, dto: DataArchivalConfiguration): Promise<any> {
        return new saveDataArchivalConfigurationCommand(db, dto).execute();
    }

    async getExpirationConfiguration(db: database) {
        return new getExpirationConfigurationCommand(db).execute();
    }

    async saveExpirationConfiguration(db: database, dto: ExpirationConfiguration) {
        return new saveExpirationConfigurationCommand(db, dto).execute();
    }

    async getTombstonesState(db: database, location: databaseLocationSpecifier) {
        return new getTombstonesStateCommand(db, location).execute();
    }

    async forceTombstonesCleanup(db: database, location: databaseLocationSpecifier) {
        return new forceTombstonesCleanupCommand(db, location).execute();
    }

    async getRevisionsForConflictsConfiguration(db: database) {
        return new getRevisionsForConflictsConfigurationCommand(db).execute();
    }

    async saveRevisionsForConflictsConfiguration(db: database, dto: RevisionsCollectionConfiguration) {
        return new saveRevisionsForConflictsConfigurationCommand(db, dto).execute();
    }

    async getRevisionsConfiguration(db: database) {
        return new getRevisionsConfigurationCommand(db).execute();
    }

    async saveRevisionsConfiguration(db: database, dto: RevisionsConfiguration) {
        return new saveRevisionsConfigurationCommand(db, dto).execute();
    }

    async enforceRevisionsConfiguration(db: database, includeForceCreated = false, collections: string[] = null) {
        return new enforceRevisionsConfigurationCommand(db, includeForceCreated, collections).execute();
    }

    async revertRevisions(db: database, dto: Raven.Server.Documents.Revisions.RevertRevisionsRequest) {
        return new revertRevisionsCommand(dto, db).execute();
    }

    async getCustomAnalyzers(db: database, getNamesOnly = false) {
        return new getCustomAnalyzersCommand(db, getNamesOnly).execute();
    }

    async deleteCustomAnalyzer(db: database, name: string) {
        return new deleteCustomAnalyzerCommand(db, name).execute();
    }

    async getCustomSorters(db: database) {
        return new getCustomSortersCommand(db).execute();
    }

    async deleteCustomSorter(db: database, name: string) {
        return new deleteCustomSorterCommand(db, name).execute();
    }

    async getDocumentsCompressionConfiguration(db: database) {
        return new getDocumentsCompressionConfigurationCommand(db).execute();
    }

    async saveDocumentsCompression(db: database, dto: Raven.Client.ServerWide.DocumentsCompressionConfiguration) {
        return new saveDocumentsCompressionCommand(db, dto).execute();
    }

    async promoteDatabaseNode(databaseName: string, nodeTag: string) {
        return new promoteDatabaseNodeCommand(databaseName, nodeTag).execute();
    }

    async getConflictSolverConfiguration(db: database) {
        return new getConflictSolverConfigurationCommand(db).execute();
    }

    async saveConflictSolverConfiguration(db: database, dto: Raven.Client.ServerWide.ConflictSolver) {
        return new saveConflictSolverConfigurationCommand(db, dto).execute();
    }

    async getConnectionStrings(db: database) {
        return new getConnectionStringsCommand(db).execute();
    }

    async saveConnectionString(db: database, connectionString: ConnectionStringDto) {
        return new saveConnectionStringCommand(db, connectionString).execute();
    }

    async deleteConnectionString(
        db: database,
        type: Raven.Client.Documents.Operations.ETL.EtlType,
        connectionStringName: string
    ) {
        return new deleteConnectionStringCommand(db, type, connectionStringName).execute();
    }

    async testClusterNodeConnection(serverUrl: string, databaseName?: string, bidirectional = true) {
        return new testClusterNodeConnectionCommand(serverUrl, databaseName, bidirectional).execute();
    }

    async testSqlConnectionString(db: database, connectionString: string, factoryName: string) {
        return new testSqlConnectionStringCommand(db, connectionString, factoryName).execute();
    }

    async testRabbitMqServerConnection(db: database, connectionString: string) {
        return new testRabbitMqServerConnectionCommand(db, connectionString).execute();
    }

    async getDatabaseRecord(db: database, reportRefreshProgress = false) {
        return new getDatabaseRecordCommand(db, reportRefreshProgress).execute();
    }

    async saveDatabaseRecord(db: database, databaseRecord: documentDto, etag: number) {
        return new saveDatabaseRecordCommand(db, databaseRecord, etag).execute();
    }
}
