using Catalog;
using Inventory.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CoreWebForms.UnitTests
{
    public class StockRulesTests
    {
        private static PostgresException Pg(string sqlState) =>
            new("test error", "ERROR", "ERROR", sqlState);

        private static SqliteException Sqlite(int code, string message) =>
            new(message, code, code);

        // --- IsTransientLock: postgres lock states ------------------------

        [Theory]
        [InlineData("55P03")]
        [InlineData("40P01")]
        [InlineData("53300")]
        public void TransientLock_AcceptsPostgresLockStates(string sqlState)
        {
            Assert.True(StockRules.IsTransientLock(Pg(sqlState)));
        }

        [Fact]
        public void TransientLock_WalksInnerExceptionChain()
        {
            var deep = new InvalidOperationException("outer",
                new DbUpdateException("middle", Pg("53300")));
            Assert.True(StockRules.IsTransientLock(deep));
        }

        [Fact]
        public void TransientLock_RejectsNonLockPostgresState()
        {
            Assert.False(StockRules.IsTransientLock(Pg("23505")));
        }

        // --- IsTransientLock: sqlite busy ----------------------------------

        [Fact]
        public void TransientLock_AcceptsSqliteBusy()
        {
            Assert.True(StockRules.IsTransientLock(Sqlite(5, "database is locked")));
        }

        [Fact]
        public void TransientLock_RejectsSqliteConstraint()
        {
            Assert.False(StockRules.IsTransientLock(Sqlite(19, "UNIQUE constraint failed: ReservationKeys")));
        }

        [Fact]
        public void TransientLock_RejectsPlainException()
        {
            Assert.False(StockRules.IsTransientLock(new InvalidOperationException("no db here")));
        }

        // --- IsUniqueViolation ----------------------------------------------

        [Fact]
        public void UniqueViolation_AcceptsPostgres23505()
        {
            var ex = new DbUpdateException("conflict", Pg("23505"));
            Assert.True(StockRules.IsUniqueViolation(ex, "ReservationKeys"));
            Assert.True(StockRules.IsUniqueViolation(ex, "ReleaseKeys"));
        }

        [Fact]
        public void UniqueViolation_AcceptsSqliteConstraintOnlyForNamedTable()
        {
            var ex = new DbUpdateException("conflict", Sqlite(19, "UNIQUE constraint failed: ReservationKeys"));
            Assert.True(StockRules.IsUniqueViolation(ex, "ReservationKeys"));
            Assert.False(StockRules.IsUniqueViolation(ex, "ReleaseKeys"));
        }

        [Fact]
        public void UniqueViolation_RejectsLockState()
        {
            var ex = new DbUpdateException("busy", Pg("55P03"));
            Assert.False(StockRules.IsUniqueViolation(ex, "ReservationKeys"));
        }

        [Fact]
        public void UniqueViolation_RejectsNoInnerException()
        {
            Assert.False(StockRules.IsUniqueViolation(new DbUpdateException("bare"), "ReservationKeys"));
        }

        // --- lock ordering ---------------------------------------------------

        [Fact]
        public void OrderItemsForLock_SortsAscendingByProductId()
        {
            var items = new[]
            {
                new StockItemDto { ProductId = 5, Quantity = 1 },
                new StockItemDto { ProductId = 1, Quantity = 2 },
                new StockItemDto { ProductId = 3, Quantity = 1 },
                new StockItemDto { ProductId = 1, Quantity = 4 }
            };
            Assert.Equal(new[] { 1, 1, 3, 5 }, StockRules.OrderItemsForLock(items).Select(i => i.ProductId));
        }

        [Fact]
        public void OrderedLockIds_DistinctAscending()
        {
            var items = new[]
            {
                new StockItemDto { ProductId = 5 },
                new StockItemDto { ProductId = 1 },
                new StockItemDto { ProductId = 3 },
                new StockItemDto { ProductId = 1 }
            };
            Assert.Equal(new[] { 1, 3, 5 }, StockRules.OrderedLockIds(items));
        }

        [Fact]
        public void OrderedLockIds_EmptyInputEmptyOutput()
        {
            Assert.Empty(StockRules.OrderedLockIds(Array.Empty<StockItemDto>()));
        }
    }
}
