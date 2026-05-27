-- AIStock 数据库初始化脚本
-- MySQL 8.0+

CREATE DATABASE IF NOT EXISTS `aistock` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

USE `aistock`;

-- 股票基础信息表
CREATE TABLE IF NOT EXISTS `stock_base` (
    `code` VARCHAR(20) NOT NULL COMMENT '股票代码',
    `name` VARCHAR(50) NOT NULL COMMENT '股票名称',
    `market` VARCHAR(10) NOT NULL COMMENT '市场（sh/sz/bj）',
    `industry` VARCHAR(100) DEFAULT NULL COMMENT '行业',
    `list_date` DATE DEFAULT NULL COMMENT '上市日期',
    `is_delisted` TINYINT(1) NOT NULL DEFAULT 0 COMMENT '是否退市',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    PRIMARY KEY (`code`),
    INDEX `idx_market` (`market`),
    INDEX `idx_industry` (`industry`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='股票基础信息';

-- K线数据表
CREATE TABLE IF NOT EXISTS `kline_data` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `code` VARCHAR(20) NOT NULL COMMENT '股票代码',
    `datetime` DATETIME NOT NULL COMMENT '日期时间',
    `interval` VARCHAR(20) NOT NULL COMMENT '周期（daily/weekly/monthly/1m/5m/15m/30m/60m）',
    `open` DECIMAL(12,4) NOT NULL COMMENT '开盘价（元）',
    `close` DECIMAL(12,4) NOT NULL COMMENT '收盘价（元）',
    `high` DECIMAL(12,4) NOT NULL COMMENT '最高价（元）',
    `low` DECIMAL(12,4) NOT NULL COMMENT '最低价（元）',
    `volume` BIGINT NOT NULL COMMENT '成交量（股）',
    `amount` DECIMAL(18,4) NOT NULL COMMENT '成交额（元）',
    `turnover_rate` DECIMAL(8,4) DEFAULT NULL COMMENT '换手率（%）',
    `change_percent` DECIMAL(8,4) DEFAULT NULL COMMENT '涨跌幅（%）',
    `source` VARCHAR(50) NOT NULL COMMENT '数据来源',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    PRIMARY KEY (`id`),
    UNIQUE INDEX `idx_code_datetime_interval` (`code`, `datetime`, `interval`),
    INDEX `idx_datetime` (`datetime`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='K线数据';

-- 公司关系表
CREATE TABLE IF NOT EXISTS `company_relation` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `source_company` VARCHAR(20) NOT NULL COMMENT '源公司代码',
    `target_company` VARCHAR(20) NOT NULL COMMENT '目标公司代码',
    `relation_type` VARCHAR(50) NOT NULL COMMENT '关系类型（customer/supplier/invest/controll）',
    `weight` DECIMAL(5,2) NOT NULL DEFAULT 1.00 COMMENT '权重',
    `description` VARCHAR(500) DEFAULT NULL COMMENT '描述',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    PRIMARY KEY (`id`),
    UNIQUE INDEX `idx_source_target_type` (`source_company`, `target_company`, `relation_type`),
    INDEX `idx_source_company` (`source_company`),
    INDEX `idx_target_company` (`target_company`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='公司关系';

-- 产业链表
CREATE TABLE IF NOT EXISTS `industry_chain` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `chain_name` VARCHAR(100) NOT NULL COMMENT '产业链名称',
    `parent_node` VARCHAR(100) DEFAULT NULL COMMENT '父节点',
    `child_node` VARCHAR(100) NOT NULL COMMENT '子节点',
    `level` INT NOT NULL DEFAULT 0 COMMENT '层级',
    `node_type` VARCHAR(50) DEFAULT NULL COMMENT '节点类型（company/product/technology）',
    `description` VARCHAR(500) DEFAULT NULL COMMENT '描述',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    PRIMARY KEY (`id`),
    INDEX `idx_chain_name` (`chain_name`),
    INDEX `idx_chain_hierarchy` (`chain_name`, `parent_node`, `child_node`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='产业链';

-- 公司-产业链关联表
CREATE TABLE IF NOT EXISTS `company_chain_relation` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `company_code` VARCHAR(20) NOT NULL COMMENT '公司代码',
    `chain_id` BIGINT NOT NULL COMMENT '产业链ID',
    `chain_node` VARCHAR(100) NOT NULL COMMENT '在产业链中的节点名称',
    `role` VARCHAR(50) DEFAULT NULL COMMENT '角色（upstream/midstream/downstream）',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    PRIMARY KEY (`id`),
    UNIQUE INDEX `idx_company_chain` (`company_code`, `chain_id`),
    INDEX `idx_company_code` (`company_code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='公司-产业链关联';

-- LLM模型配置表
CREATE TABLE IF NOT EXISTS `llm_model_config` (
    `id` VARCHAR(50) NOT NULL COMMENT '模型ID',
    `name` VARCHAR(100) NOT NULL COMMENT '模型名称',
    `base_url` VARCHAR(500) NOT NULL COMMENT 'API地址',
    `api_key` VARCHAR(500) NOT NULL COMMENT 'API密钥',
    `model` VARCHAR(100) NOT NULL COMMENT '模型名称',
    `is_enabled` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    `priority` INT NOT NULL DEFAULT 0 COMMENT '优先级',
    `timeout_seconds` INT NOT NULL DEFAULT 30 COMMENT '超时时间（秒）',
    `max_tokens` INT DEFAULT NULL COMMENT '最大Token数',
    `temperature` DECIMAL(3,2) DEFAULT NULL COMMENT '温度参数',
    `description` VARCHAR(500) DEFAULT NULL COMMENT '描述',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='LLM模型配置';

-- 事件记录表
CREATE TABLE IF NOT EXISTS `event_record` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `event_type` VARCHAR(50) NOT NULL COMMENT '事件类型（news/report/policy/rumor）',
    `title` VARCHAR(500) NOT NULL COMMENT '标题',
    `content` TEXT DEFAULT NULL COMMENT '内容',
    `source` VARCHAR(200) DEFAULT NULL COMMENT '来源',
    `url` VARCHAR(1000) DEFAULT NULL COMMENT '原始URL',
    `sentiment` VARCHAR(20) DEFAULT NULL COMMENT '情绪（positive/negative/neutral）',
    `sentiment_score` DECIMAL(5,4) DEFAULT NULL COMMENT '情绪分数（-1到1）',
    `importance` INT DEFAULT NULL COMMENT '重要程度（1-10）',
    `credibility` INT DEFAULT NULL COMMENT '可信度（1-10）',
    `related_stocks` VARCHAR(1000) DEFAULT NULL COMMENT '关联股票代码（逗号分隔）',
    `related_concepts` VARCHAR(1000) DEFAULT NULL COMMENT '关联概念（逗号分隔）',
    `llm_analysis` TEXT DEFAULT NULL COMMENT 'LLM分析结果',
    `event_time` DATETIME DEFAULT NULL COMMENT '事件时间',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    PRIMARY KEY (`id`),
    INDEX `idx_event_type` (`event_type`),
    INDEX `idx_event_time` (`event_time`),
    INDEX `idx_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='事件记录';

-- 交易记录表
CREATE TABLE IF NOT EXISTS `trade_record` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `order_id` BIGINT DEFAULT NULL COMMENT '订单ID',
    `agree_id` BIGINT DEFAULT NULL COMMENT '委托编号',
    `code` VARCHAR(20) NOT NULL COMMENT '股票代码',
    `name` VARCHAR(50) DEFAULT NULL COMMENT '股票名称',
    `direction` VARCHAR(10) NOT NULL COMMENT '交易方向（buy/sell）',
    `price` DECIMAL(12,4) NOT NULL COMMENT '价格（元）',
    `volume` BIGINT NOT NULL COMMENT '数量（股）',
    `amount` DECIMAL(18,4) NOT NULL COMMENT '金额（元）',
    `commission` DECIMAL(12,4) NOT NULL DEFAULT 0 COMMENT '手续费（元）',
    `strategy_name` VARCHAR(100) DEFAULT NULL COMMENT '策略名称',
    `signal_id` VARCHAR(100) DEFAULT NULL COMMENT '信号ID',
    `status` VARCHAR(20) NOT NULL DEFAULT 'pending' COMMENT '状态（pending/accepted/completed/failed/cancelled）',
    `status_message` VARCHAR(500) DEFAULT NULL COMMENT '状态消息',
    `trade_time` DATETIME DEFAULT NULL COMMENT '交易时间',
    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    PRIMARY KEY (`id`),
    INDEX `idx_code` (`code`),
    INDEX `idx_order_id` (`order_id`),
    INDEX `idx_status` (`status`),
    INDEX `idx_created_at` (`created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='交易记录';

-- 持仓记录表
CREATE TABLE IF NOT EXISTS `position` (
    `id` BIGINT NOT NULL AUTO_INCREMENT COMMENT '主键',
    `code` VARCHAR(20) NOT NULL COMMENT '股票代码',
    `name` VARCHAR(50) DEFAULT NULL COMMENT '股票名称',
    `volume` BIGINT NOT NULL COMMENT '持股数量（股）',
    `sellable_volume` BIGINT NOT NULL DEFAULT 0 COMMENT '可卖数量（股）',
    `cost_price` DECIMAL(12,4) NOT NULL COMMENT '成本价（元）',
    `current_price` DECIMAL(12,4) NOT NULL DEFAULT 0 COMMENT '现价（元）',
    `profit` DECIMAL(18,4) NOT NULL DEFAULT 0 COMMENT '盈亏金额（元）',
    `profit_rate` DECIMAL(8,4) NOT NULL DEFAULT 0 COMMENT '盈亏比例（%）',
    `strategy_name` VARCHAR(100) DEFAULT NULL COMMENT '策略名称',
    `buy_time` DATETIME DEFAULT NULL COMMENT '买入时间',
    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP COMMENT '更新时间',
    PRIMARY KEY (`id`),
    UNIQUE INDEX `idx_code` (`code`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='持仓记录';

-- 插入默认LLM模型配置（示例）
INSERT INTO `llm_model_config` (`id`, `name`, `base_url`, `api_key`, `model`, `is_enabled`, `priority`, `timeout_seconds`, `max_tokens`, `temperature`, `description`) VALUES
('tongyi', '通义千问', 'https://dashscope.aliyuncs.com/compatible-mode/v1', 'sk-your-api-key', 'qwen-plus', 1, 1, 30, 4096, 0.70, '阿里云通义千问'),
('deepseek', 'DeepSeek', 'https://api.deepseek.com/v1', 'sk-your-api-key', 'deepseek-chat', 1, 2, 30, 4096, 0.70, 'DeepSeek模型'),
('local', '本地模型', 'http://localhost:11434/v1', '', 'qwen2.5:14b', 1, 3, 60, 4096, 0.70, '本地Ollama模型');
