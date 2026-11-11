using System.Data;
using Dapper;

namespace DisputePortal.Platform.Infrastructure;

public static class PlatformMapping
{
    private static int initialized;

    public static void InitializeDapperMappings()
    {
        if (Interlocked.Exchange(ref initialized, 1) == 0)
        {
            SqlMapper.AddTypeHandler(new UtcTimestampHandler());
            SqlMapper.AddTypeHandler(new CalendarDateHandler());
        }
    }

    private class CalendarDateHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly date => date,
            DateTime date => DateOnly.FromDateTime(date),
            _ => throw new DataException("Expected a calendar date.")
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }

    private class UtcTimestampHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override DateTimeOffset Parse(object value) => value switch
        {
            DateTimeOffset instant => instant.ToUniversalTime(),
            DateTime instant => new DateTimeOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc)),
            _ => throw new DataException("Expected a UTC database timestamp.")
        };

        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.DbType = DbType.DateTimeOffset;
            parameter.Value = value.ToUniversalTime();
        }
    }
}
