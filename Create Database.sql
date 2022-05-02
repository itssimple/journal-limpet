USE [master]
GO

/****** Object:  Database [journal-limpet]    Script Date: 2022-05-02 16:08:28 ******/
CREATE DATABASE [journal-limpet]
 CONTAINMENT = NONE
 ON  PRIMARY
( NAME = N'journal-limpet', FILENAME = N'/var/opt/mssql/data/journal-limpet.mdf' , SIZE = 40411136KB , MAXSIZE = UNLIMITED, FILEGROWTH = 65536KB )
 LOG ON
( NAME = N'journal-limpet_log', FILENAME = N'/var/opt/mssql/data/journal-limpet_log.ldf' , SIZE = 28123136KB , MAXSIZE = 2048GB , FILEGROWTH = 65536KB )
 WITH CATALOG_COLLATION = DATABASE_DEFAULT
GO

IF (1 = FULLTEXTSERVICEPROPERTY('IsFullTextInstalled'))
begin
EXEC [journal-limpet].[dbo].[sp_fulltext_database] @action = 'enable'
end
GO

ALTER DATABASE [journal-limpet] SET ANSI_NULL_DEFAULT OFF
GO

ALTER DATABASE [journal-limpet] SET ANSI_NULLS OFF
GO

ALTER DATABASE [journal-limpet] SET ANSI_PADDING OFF
GO

ALTER DATABASE [journal-limpet] SET ANSI_WARNINGS OFF
GO

ALTER DATABASE [journal-limpet] SET ARITHABORT OFF
GO

ALTER DATABASE [journal-limpet] SET AUTO_CLOSE OFF
GO

ALTER DATABASE [journal-limpet] SET AUTO_SHRINK OFF
GO

ALTER DATABASE [journal-limpet] SET AUTO_UPDATE_STATISTICS ON
GO

ALTER DATABASE [journal-limpet] SET CURSOR_CLOSE_ON_COMMIT OFF
GO

ALTER DATABASE [journal-limpet] SET CURSOR_DEFAULT  GLOBAL
GO

ALTER DATABASE [journal-limpet] SET CONCAT_NULL_YIELDS_NULL OFF
GO

ALTER DATABASE [journal-limpet] SET NUMERIC_ROUNDABORT OFF
GO

ALTER DATABASE [journal-limpet] SET QUOTED_IDENTIFIER OFF
GO

ALTER DATABASE [journal-limpet] SET RECURSIVE_TRIGGERS OFF
GO

ALTER DATABASE [journal-limpet] SET  DISABLE_BROKER
GO

ALTER DATABASE [journal-limpet] SET AUTO_UPDATE_STATISTICS_ASYNC OFF
GO

ALTER DATABASE [journal-limpet] SET DATE_CORRELATION_OPTIMIZATION OFF
GO

ALTER DATABASE [journal-limpet] SET TRUSTWORTHY OFF
GO

ALTER DATABASE [journal-limpet] SET ALLOW_SNAPSHOT_ISOLATION OFF
GO

ALTER DATABASE [journal-limpet] SET PARAMETERIZATION SIMPLE
GO

ALTER DATABASE [journal-limpet] SET READ_COMMITTED_SNAPSHOT OFF
GO

ALTER DATABASE [journal-limpet] SET HONOR_BROKER_PRIORITY OFF
GO

ALTER DATABASE [journal-limpet] SET RECOVERY SIMPLE
GO

ALTER DATABASE [journal-limpet] SET  MULTI_USER
GO

ALTER DATABASE [journal-limpet] SET PAGE_VERIFY CHECKSUM
GO

ALTER DATABASE [journal-limpet] SET DB_CHAINING OFF
GO

ALTER DATABASE [journal-limpet] SET FILESTREAM( NON_TRANSACTED_ACCESS = OFF )
GO

ALTER DATABASE [journal-limpet] SET TARGET_RECOVERY_TIME = 60 SECONDS
GO

ALTER DATABASE [journal-limpet] SET DELAYED_DURABILITY = DISABLED
GO

ALTER DATABASE [journal-limpet] SET ACCELERATED_DATABASE_RECOVERY = OFF
GO

ALTER DATABASE [journal-limpet] SET QUERY_STORE = OFF
GO

ALTER DATABASE [journal-limpet] SET  READ_WRITE
GO

USE [journal-limpet]
GO

/****** Object:  Table [dbo].[EliteSystem]    Script Date: 2022-05-02 16:09:38 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[EliteSystem](
	[SystemAddress] [bigint] NOT NULL,
	[StarSystem] [nvarchar](512) NOT NULL,
	[StarPos] [nvarchar](512) NOT NULL,
	[vX]  AS (CONVERT([float],json_value([StarPos],'$.x'))) PERSISTED,
	[vY]  AS (CONVERT([float],json_value([StarPos],'$.y'))) PERSISTED,
	[vZ]  AS (CONVERT([float],json_value([StarPos],'$.z'))) PERSISTED
) ON [PRIMARY]
GO

EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Small table to keep track of Elite Dangerous systems' , @level0type=N'SCHEMA',@level0name=N'dbo', @level1type=N'TABLE',@level1name=N'EliteSystem'
GO

/****** Object:  Table [dbo].[user_journal]    Script Date: 2022-05-02 16:10:04 ******/

CREATE TABLE [dbo].[user_journal](
	[journal_id] [bigint] IDENTITY(1,1) NOT NULL,
	[user_identifier] [uniqueidentifier] NOT NULL,
	[created] [datetime2](7) NOT NULL,
	[journal_date] [datetime2](7) NOT NULL,
	[s3_path] [varchar](512) NOT NULL,
	[last_processed_line] [nvarchar](max) NULL,
	[last_processed_line_number] [bigint] NULL,
	[complete_entry] [bit] NOT NULL,
	[last_update] [datetime2](7) NULL,
	[sent_to_eddn] [bit] NOT NULL,
	[sent_to_eddn_line] [int] NOT NULL,
	[integration_data] [nvarchar](max) NULL,
 CONSTRAINT [user_journal_pk] PRIMARY KEY CLUSTERED
(
	[journal_id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[user_journal] SET (LOCK_ESCALATION = AUTO)
GO

ALTER TABLE [dbo].[user_journal] ADD  CONSTRAINT [DF__user_jour__creat__286302EC]  DEFAULT (getutcdate()) FOR [created]
GO

ALTER TABLE [dbo].[user_journal] ADD  CONSTRAINT [DF__user_jour__compl__2A4B4B5E]  DEFAULT ((0)) FOR [complete_entry]
GO

ALTER TABLE [dbo].[user_journal] ADD  CONSTRAINT [DF__user_jour__sent___2D27B809]  DEFAULT ((0)) FOR [sent_to_eddn]
GO

ALTER TABLE [dbo].[user_journal] ADD  CONSTRAINT [DF__user_jour__sent___2E1BDC42]  DEFAULT ((0)) FOR [sent_to_eddn_line]
GO

/****** Object:  Table [dbo].[user_profile]    Script Date: 2022-05-02 16:10:25 ******/

CREATE TABLE [dbo].[user_profile](
	[user_identifier] [uniqueidentifier] NOT NULL,
	[created] [datetime2](7) NOT NULL,
	[deleted] [bit] NOT NULL,
	[deletion_date] [datetime2](7) NULL,
	[user_settings] [nvarchar](max) NULL,
	[notification_email] [varchar](1024) NULL,
	[last_notification_mail] [datetime2](7) NULL,
	[send_to_eddn] [bit] NOT NULL,
	[skip_download] [bit] NOT NULL,
	[integration_settings] [nvarchar](max) NULL,
	[latest_fetched_journal_date] [datetime2](7) NULL,
 CONSTRAINT [user_profile_pk] PRIMARY KEY CLUSTERED
(
	[user_identifier] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
GO

ALTER TABLE [dbo].[user_profile] ADD  DEFAULT (newid()) FOR [user_identifier]
GO

ALTER TABLE [dbo].[user_profile] ADD  DEFAULT (getutcdate()) FOR [created]
GO

ALTER TABLE [dbo].[user_profile] ADD  DEFAULT ((0)) FOR [deleted]
GO

ALTER TABLE [dbo].[user_profile] ADD  CONSTRAINT [DF__user_prof__send___2F10007B]  DEFAULT ((0)) FOR [send_to_eddn]
GO

ALTER TABLE [dbo].[user_profile] ADD  DEFAULT ((0)) FOR [skip_download]
GO