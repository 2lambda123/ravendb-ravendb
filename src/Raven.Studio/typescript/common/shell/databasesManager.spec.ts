/**
 * @jest-environment jsdom
 */

import databasesManager from "common/shell/databasesManager";
import endpointConstants from "endpoints";
import shardedDatabase from "models/resources/shardedDatabase";
import databaseShard from "models/resources/databaseShard";
import { ajaxMock } from "../../test/mocks";
import { DatabaseStubs } from "../../test/DatabaseStubs";
import nonShardedDatabase from "models/resources/nonShardedDatabase";

describe("databasesManager", () => {
    
    beforeEach(() => {
        jest.clearAllMocks();
    });
    
    it("can handle non-sharded database", async () => {
        const response = DatabaseStubs.singleDatabaseResponse();
        
        ajaxMock.mockImplementation((args: JQueryAjaxSettings) => {
            if (args.url === endpointConstants.global.databases.databases) {
                return $.Deferred<typeof response>().resolve(response);
            }
        });

        const manager = new databasesManager();
        await manager.init();

        const dbs = manager.databases();
        expect(dbs)
            .toHaveLength(1);

        const firstDb = dbs[0];
        expect(firstDb)
            .toBeInstanceOf(nonShardedDatabase);
        expect(firstDb.name)
            .toEqual(response.Databases[0].Name);
    })
    
    it("can handle sharded database", async () => {
        const response = DatabaseStubs.shardedDatabasesResponse();
        
        ajaxMock.mockImplementation((args: JQueryAjaxSettings) => {
            if (args.url === endpointConstants.global.databases.databases) {
                return $.Deferred<typeof response>().resolve(response);
            }
        });
        
        const manager = new databasesManager();
        await manager.init();
        
        const dbs = manager.databases();
        
        expect(dbs)
            .toHaveLength(1);
        
        const expectedShardedDatabaseGroup = (response.Databases[0].Name.split("$")[0]);
        
        const firstDb = dbs[0];
        expect(firstDb)
            .toBeInstanceOf(shardedDatabase);
        expect(firstDb.name)
            .toEqual(expectedShardedDatabaseGroup);
        
        const sharded = firstDb as shardedDatabase;
        const shards = sharded.shards();
        expect(shards)
            .toHaveLength(2);
        
        expect(shards[0].name)
            .toEqual(response.Databases[0].Name);
        expect(shards[0])
            .toBeInstanceOf(databaseShard);

        expect(shards[1].name)
            .toEqual(response.Databases[1].Name);
        expect(shards[1])
            .toBeInstanceOf(databaseShard);
    });
    
    it("can get single shard by name", async () => {
        const response = DatabaseStubs.shardedDatabasesResponse();

        ajaxMock.mockImplementation((args: JQueryAjaxSettings) => {
            if (args.url === endpointConstants.global.databases.databases) {
                return $.Deferred<typeof response>().resolve(response);
            }
        });

        const manager = new databasesManager();
        await manager.init();
        
        const firstShardName = response.Databases[0].Name;
        
        const shard = manager.getDatabaseByName(firstShardName) as databaseShard;
        
        expect(shard)
            .not.toBeNull();
        expect(shard)
            .toBeInstanceOf(databaseShard);
        
        const shardGroup = shard.parent;
        expect(shardGroup)
            .not.toBeNull();
        expect(shardGroup)
            .toBeInstanceOf(shardedDatabase);
        expect(shardGroup.shards())
            .toHaveLength(2);
    });

    it("can get sharded database by name", async () => {
        const response = DatabaseStubs.shardedDatabasesResponse();

        ajaxMock.mockImplementation((args: JQueryAjaxSettings) => {
            if (args.url === endpointConstants.global.databases.databases) {
                return $.Deferred<typeof response>().resolve(response);
            }
        });

        const manager = new databasesManager();
        await manager.init();

        const shardGroupName = response.Databases[0].Name.split("$")[0];

        const shard = manager.getDatabaseByName(shardGroupName) as shardedDatabase;

        expect(shard)
            .not.toBeNull();
        expect(shard)
            .toBeInstanceOf(shardedDatabase);
        expect(shard.shards())
            .toHaveLength(2);
    });
})


