﻿using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqliteDbContext.Helpers;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace SqliteDbContext.Context
{
    public class SqliteDbContext<T> where T : DbContext
    {
        private BogusGenerator bogus;
        private T? context;
        private static IDictionary<Type, Delegate> postDependencyResolvers = new Dictionary<Type, Delegate>();
        public T? Context => context;
        private DbContextOptions<T> _options;
        public DbContextOptions<T> Options => _options;

        public SqliteDbContext(string? DbInstanceName = null, SqliteConnection? conn = null)
        {
            CreateConnection(DbInstanceName, conn);
            bogus = new BogusGenerator(context);
        }

        private void CreateConnection(string? dbIntanceName, SqliteConnection? conn)
        {
            dbIntanceName = dbIntanceName ?? Guid.NewGuid().ToString();
            if(conn == null)
            {
                var config = new SqliteConnectionStringBuilder { DataSource = $"{dbIntanceName}:memory:", Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared };
                conn = new SqliteConnection(config.ToString());
            }

            if(conn.State != System.Data.ConnectionState.Open)
            {
                conn.Open();
            }

            _options = new DbContextOptionsBuilder<T>()
                .UseSqlite(conn)
                .Options;

            context = (T?)Activator.CreateInstance(typeof(T), _options);
            context?.Database.EnsureDeleted();
            context?.Database.EnsureCreated();
        }

        public static void RegisterKeyAssignment<E>(Action<E, IKeySeeder> dependencyActionResolver) where E : class
            => postDependencyResolvers.TryAdd(typeof(E), dependencyActionResolver);

        public List<E> GenerateEntities<E>(int count, Action<E>? initializeAction = null) where E : class
        {
            var list = new List<E>();
            for(int i = 0; i < count; i++)
            {
                list.Add(GenerateEntity(initializeAction));
            }
            return list;
        }

        public E GenerateEntity<E>(Action<E>? initializeAction = null) where E : class
        {
            var type = typeof(E);
            if (!postDependencyResolvers.ContainsKey(type))
                throw new Exception($"Must have registered dependency resolver for {type.Name} prior to saving");
            var entity = bogus.Generate<E>();
            bogus.RemoveGeneratedReferences(entity);
            bogus.ClearKeys(entity);
            bogus.ApplyInitializingAction(entity, initializeAction);
            var search = context?.Set<E>()?.Find(entity.GetKeys());
            if (search == null)
            {
                //assumes all keys are untouched
                if (entity.GetKeys().Any(x => x.ToString() == "-1" || x.ToString() == null))
                {
                    //validation that all keys are untouched or are warns user that keys are incorrectly assigned
                    if (!entity.GetKeys().All(x => x.ToString() == "-1" || x.ToString() == null))
                        throw new Exception($"Didn't update all keys required to override autogeneration");
                    do //if entity is found, then it was generated ahead of time - skip and generate next valid entity
                    {
                        bogus.ApplyDependencyAction(entity, (Action<E, IKeySeeder>)postDependencyResolvers[type]);
                        search = context?.Set<E>()?.Find(entity.GetKeys());
                    } while (search != null);
                }
                else //all keys must be initialized in order to override autogeneration - assumes user will handle dependencies outside of what is provided
                {
                    bogus.ApplyInitializingAction(entity, initializeAction);
                }
                search = context?.Set<E>()?.Find(entity.GetKeys());
                context?.Add(entity);
            } //all keys match and found existing item
            else
            {
                bogus.ApplyInitializingAction(search, initializeAction);
            }
            context?.SaveChanges();
            return entity;
        }
    }
}