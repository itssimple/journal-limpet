﻿using System;
using System.Data;

namespace Journal_Limpet.Shared.Models.Journal
{
    public class UserJournal
    {
        public int JournalId { get; }
        public Guid UserIdentifier { get; }
        public DateTimeOffset Created { get; }
        public DateTimeOffset JournalDate { get; }
        public string S3Path { get; }
        public string LastProcessedLine { get; }
        public int? LastProcessedLineNumber { get; }
        public bool CompleteEntry { get; }
        public DateTimeOffset? LastUpdate { get; }

        public UserJournal(DataRow row)
        {
            JournalId = row.Field<int>("journal_id");
            UserIdentifier = row.Field<Guid>("user_identifier");
            Created = new DateTimeOffset(row.Field<DateTime>("created"), TimeSpan.Zero);
            JournalDate = new DateTimeOffset(row.Field<DateTime>("journal_date"), TimeSpan.Zero);
            S3Path = row["s3_path"].ToString();
            LastProcessedLine = row["last_processed_line"].ToString();
            LastProcessedLineNumber = row.Field<int?>("last_processed_line_number");
            CompleteEntry = row.Field<bool>("complete_entry");
            LastUpdate = !row.IsNull("last_update") ? new DateTimeOffset(row.Field<DateTime>("last_update"), TimeSpan.Zero) as DateTimeOffset? : null;
        }
    }
}