﻿// <auto-generated />
using System;
using CoreComponents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;

namespace CoreComponents.Migrations
{
    [DbContext(typeof(PraxisContext))]
    [Migration("20201025163737_maptileTableindex")]
    partial class maptileTableindex
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .UseIdentityColumns()
                .HasAnnotation("Relational:MaxIdentifierLength", 128)
                .HasAnnotation("ProductVersion", "5.0.0");

            modelBuilder.Entity("DatabaseAccess.DbTables+AreaType", b =>
                {
                    b.Property<int>("AreaTypeId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .UseIdentityColumn();

                    b.Property<string>("AreaName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("HtmlColorCode")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("OsmTags")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("AreaTypeId");

                    b.ToTable("AreaTypes");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+MapData", b =>
                {
                    b.Property<long>("MapDataId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .UseIdentityColumn();

                    b.Property<int>("AreaTypeId")
                        .HasColumnType("int");

                    b.Property<long?>("NodeId")
                        .HasColumnType("bigint");

                    b.Property<long?>("RelationId")
                        .HasColumnType("bigint");

                    b.Property<long?>("WayId")
                        .HasColumnType("bigint");

                    b.Property<string>("name")
                        .HasColumnType("nvarchar(max)");

                    b.Property<Geometry>("place")
                        .HasColumnType("geography");

                    b.Property<string>("type")
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("MapDataId");

                    b.HasIndex("WayId");

                    b.ToTable("MapData");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+MapTile", b =>
                {
                    b.Property<long>("MapTileId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .UseIdentityColumn();

                    b.Property<string>("PlusCode")
                        .HasMaxLength(12)
                        .HasColumnType("nvarchar(12)");

                    b.Property<bool>("regenerate")
                        .HasColumnType("bit");

                    b.Property<int>("resolutionScale")
                        .HasColumnType("int");

                    b.Property<byte[]>("tileData")
                        .HasColumnType("varbinary(max)");

                    b.HasKey("MapTileId");

                    b.HasIndex("PlusCode");

                    b.ToTable("MapTiles");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+PerformanceInfo", b =>
                {
                    b.Property<int>("PerformanceInfoID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .UseIdentityColumn();

                    b.Property<DateTime>("calledAt")
                        .HasColumnType("datetime2");

                    b.Property<string>("functionName")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("notes")
                        .HasColumnType("nvarchar(max)");

                    b.Property<long>("runTime")
                        .HasColumnType("bigint");

                    b.HasKey("PerformanceInfoID");

                    b.ToTable("PerformanceInfo");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+PlayerData", b =>
                {
                    b.Property<int>("PlayerDataID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int")
                        .UseIdentityColumn();

                    b.Property<int>("DateLastTrophyBought")
                        .HasColumnType("int");

                    b.Property<int>("altitudeSpread")
                        .HasColumnType("int");

                    b.Property<int>("cellVisits")
                        .HasColumnType("int");

                    b.Property<string>("deviceID")
                        .HasColumnType("nvarchar(450)");

                    b.Property<double>("distance")
                        .HasColumnType("float");

                    b.Property<DateTime>("lastSyncTime")
                        .HasColumnType("datetime2");

                    b.Property<double>("maxSpeed")
                        .HasColumnType("float");

                    b.Property<int>("score")
                        .HasColumnType("int");

                    b.Property<int>("t10Cells")
                        .HasColumnType("int");

                    b.Property<int>("t8Cells")
                        .HasColumnType("int");

                    b.Property<int>("timePlayed")
                        .HasColumnType("int");

                    b.Property<double>("totalSpeed")
                        .HasColumnType("float");

                    b.HasKey("PlayerDataID");

                    b.HasIndex("deviceID");

                    b.ToTable("PlayerData");
                });

            modelBuilder.Entity("DatabaseAccess.DbTables+PremadeResults", b =>
                {
                    b.Property<long>("PremadeResultsId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("bigint")
                        .UseIdentityColumn();

                    b.Property<string>("Data")
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("PlusCode6")
                        .HasMaxLength(6)
                        .HasColumnType("nvarchar(6)");

                    b.HasKey("PremadeResultsId");

                    b.HasIndex("PlusCode6");

                    b.ToTable("PremadeResults");
                });
#pragma warning restore 612, 618
        }
    }
}
