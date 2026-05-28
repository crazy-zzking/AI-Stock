CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
);

START TRANSACTION;
IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `company_chain_relation` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `company_code` varchar(20) NOT NULL,
        `chain_id` bigint NOT NULL,
        `chain_node` varchar(100) NOT NULL,
        `role` varchar(50) NULL,
        `created_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `company_relation` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `source_company` varchar(20) NOT NULL,
        `target_company` varchar(20) NOT NULL,
        `relation_type` varchar(50) NOT NULL,
        `weight` decimal(18,2) NOT NULL,
        `description` varchar(500) NULL,
        `created_at` datetime(6) NOT NULL,
        `updated_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `event_record` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `event_type` varchar(50) NOT NULL,
        `title` varchar(500) NOT NULL,
        `content` longtext NULL,
        `source` varchar(200) NULL,
        `url` varchar(1000) NULL,
        `sentiment` varchar(20) NULL,
        `sentiment_score` decimal(18,2) NULL,
        `importance` int NULL,
        `credibility` int NULL,
        `related_stocks` varchar(1000) NULL,
        `related_concepts` varchar(1000) NULL,
        `llm_analysis` longtext NULL,
        `event_time` datetime(6) NULL,
        `created_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `industry_chain` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `chain_name` varchar(100) NOT NULL,
        `parent_node` varchar(100) NULL,
        `child_node` varchar(100) NOT NULL,
        `level` int NOT NULL,
        `node_type` varchar(50) NULL,
        `description` varchar(500) NULL,
        `created_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `kline_data` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `code` varchar(20) NOT NULL,
        `datetime` datetime(6) NOT NULL,
        `interval` varchar(20) NOT NULL,
        `open` decimal(18,2) NOT NULL,
        `close` decimal(18,2) NOT NULL,
        `high` decimal(18,2) NOT NULL,
        `low` decimal(18,2) NOT NULL,
        `volume` bigint NOT NULL,
        `amount` decimal(18,2) NOT NULL,
        `turnover_rate` decimal(18,2) NULL,
        `change_percent` decimal(18,2) NULL,
        `source` varchar(50) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `llm_model_config` (
        `id` varchar(50) NOT NULL,
        `name` varchar(100) NOT NULL,
        `base_url` varchar(500) NOT NULL,
        `api_key` varchar(500) NOT NULL,
        `model` varchar(100) NOT NULL,
        `is_enabled` tinyint(1) NOT NULL,
        `priority` int NOT NULL,
        `timeout_seconds` int NOT NULL,
        `max_tokens` int NULL,
        `temperature` decimal(18,2) NULL,
        `description` varchar(500) NULL,
        `created_at` datetime(6) NOT NULL,
        `updated_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `position` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `code` varchar(20) NOT NULL,
        `name` varchar(50) NULL,
        `volume` bigint NOT NULL,
        `sellable_volume` bigint NOT NULL,
        `cost_price` decimal(18,2) NOT NULL,
        `current_price` decimal(18,2) NOT NULL,
        `profit` decimal(18,2) NOT NULL,
        `profit_rate` decimal(18,2) NOT NULL,
        `strategy_name` varchar(100) NULL,
        `buy_time` datetime(6) NULL,
        `updated_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `stock_base` (
        `code` varchar(20) NOT NULL,
        `name` varchar(50) NOT NULL,
        `market` varchar(10) NOT NULL,
        `industry` varchar(100) NULL,
        `list_date` datetime(6) NULL,
        `is_delisted` tinyint(1) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        `updated_at` datetime(6) NOT NULL,
        PRIMARY KEY (`code`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `trade_record` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `order_id` bigint NULL,
        `agree_id` bigint NULL,
        `code` varchar(20) NOT NULL,
        `name` varchar(50) NULL,
        `direction` varchar(10) NOT NULL,
        `price` decimal(18,2) NOT NULL,
        `volume` bigint NOT NULL,
        `amount` decimal(18,2) NOT NULL,
        `commission` decimal(18,2) NOT NULL,
        `strategy_name` varchar(100) NULL,
        `signal_id` varchar(100) NULL,
        `status` varchar(20) NOT NULL,
        `status_message` varchar(500) NULL,
        `trade_time` datetime(6) NULL,
        `created_at` datetime(6) NOT NULL,
        `updated_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`)
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `event_concept_relation` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `event_id` bigint NOT NULL,
        `concept_name` varchar(100) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`),
        CONSTRAINT `FK_event_concept_relation_event_record_event_id` FOREIGN KEY (`event_id`) REFERENCES `event_record` (`id`) ON DELETE CASCADE
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE TABLE `event_stock_relation` (
        `id` bigint NOT NULL AUTO_INCREMENT,
        `event_id` bigint NOT NULL,
        `stock_code` varchar(20) NOT NULL,
        `created_at` datetime(6) NOT NULL,
        PRIMARY KEY (`id`),
        CONSTRAINT `FK_event_stock_relation_event_record_event_id` FOREIGN KEY (`event_id`) REFERENCES `event_record` (`id`) ON DELETE CASCADE
    );
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_company_chain_relation_company_code` ON `company_chain_relation` (`company_code`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX `IX_company_chain_relation_company_code_chain_id` ON `company_chain_relation` (`company_code`, `chain_id`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_company_relation_source_company` ON `company_relation` (`source_company`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX `IX_company_relation_source_company_target_company_relation_type` ON `company_relation` (`source_company`, `target_company`, `relation_type`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_company_relation_target_company` ON `company_relation` (`target_company`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_event_concept_relation_concept_name` ON `event_concept_relation` (`concept_name`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX `IX_event_concept_relation_event_id_concept_name` ON `event_concept_relation` (`event_id`, `concept_name`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_event_record_created_at` ON `event_record` (`created_at`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_event_record_event_time` ON `event_record` (`event_time`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_event_record_event_type` ON `event_record` (`event_type`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX `IX_event_stock_relation_event_id_stock_code` ON `event_stock_relation` (`event_id`, `stock_code`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_event_stock_relation_stock_code` ON `event_stock_relation` (`stock_code`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_industry_chain_chain_name` ON `industry_chain` (`chain_name`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_industry_chain_chain_name_parent_node_child_node` ON `industry_chain` (`chain_name`, `parent_node`, `child_node`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX `IX_kline_data_code_datetime_interval` ON `kline_data` (`code`, `datetime`, `interval`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_kline_data_datetime` ON `kline_data` (`datetime`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE UNIQUE INDEX `IX_position_code` ON `position` (`code`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_stock_base_industry` ON `stock_base` (`industry`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_stock_base_market` ON `stock_base` (`market`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_trade_record_code` ON `trade_record` (`code`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_trade_record_created_at` ON `trade_record` (`created_at`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_trade_record_order_id` ON `trade_record` (`order_id`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    CREATE INDEX `IX_trade_record_status` ON `trade_record` (`status`);
END;

IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260528144614_InitialCreate')
BEGIN
    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20260528144614_InitialCreate', '9.0.0');
END;

COMMIT;

