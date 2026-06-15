import React, { useCallback, useEffect, useState } from 'react';
import { Card, Tabs, Tag, Pagination, Empty, Spin, Typography, Space, message } from 'antd';
import {
  ClockCircleOutlined, LinkOutlined, StockOutlined, TagsOutlined,
} from '@ant-design/icons';
import { getEventsPaged } from '../api';
import type { EventListItemDto } from '../api';

const { Paragraph, Text, Link } = Typography;

/** 事件类型 → 显示名 + 颜色 */
const TYPE_META: Record<string, { label: string; color: string }> = {
  news: { label: '新闻', color: 'blue' },
  report: { label: '研报', color: 'purple' },
  policy: { label: '政策', color: 'gold' },
  'knowledge-star': { label: '知识星球', color: 'magenta' },
};

const TYPE_TABS = [
  { key: '', label: '全部' },
  { key: 'news', label: '新闻' },
  { key: 'report', label: '研报' },
  { key: 'policy', label: '政策' },
  { key: 'knowledge-star', label: '知识星球' },
];

/** 情绪 → 标签（A股惯例：正面=红、负面=绿） */
const sentimentTag = (sentiment: string | null, score: number | null) => {
  if (!sentiment) return null;
  const map: Record<string, { label: string; color: string }> = {
    positive: { label: '正面', color: 'red' },
    negative: { label: '负面', color: 'green' },
    neutral: { label: '中性', color: 'default' },
  };
  const m = map[sentiment] ?? { label: sentiment, color: 'default' };
  const suffix = score != null ? ` ${score > 0 ? '+' : ''}${score.toFixed(2)}` : '';
  return <Tag color={m.color}>{m.label}{suffix}</Tag>;
};

const fmtTime = (t: string | null) => {
  if (!t) return '—';
  const d = new Date(t);
  if (isNaN(d.getTime())) return t;
  return d.toLocaleString('zh-CN', { hour12: false });
};

const typeTag = (type: string) => {
  const m = TYPE_META[type] ?? { label: type, color: 'default' };
  return <Tag color={m.color}>{m.label}</Tag>;
};

const PAGE_SIZE = 20;

/** 情报事件 — event_record 按类型/时间分页展示，含关联个股与概念 */
const Intelligence: React.FC = () => {
  const [eventType, setEventType] = useState('');
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [items, setItems] = useState<EventListItemDto[]>([]);
  const [loading, setLoading] = useState(false);

  const fetchData = useCallback((type: string, p: number) => {
    setLoading(true);
    getEventsPaged(p, PAGE_SIZE, type)
      .then((r) => {
        setItems(r.data.items);
        setTotal(r.data.total);
      })
      .catch(() => message.error('加载情报事件失败'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    fetchData(eventType, page);
  }, [eventType, page, fetchData]);

  const onTypeChange = (key: string) => {
    setEventType(key);
    setPage(1);
  };

  return (
    <div>
      <Tabs activeKey={eventType} onChange={onTypeChange} items={TYPE_TABS} />

      <Spin spinning={loading}>
        {items.length === 0 && !loading ? (
          <Empty description="暂无情报事件" style={{ marginTop: 48 }} />
        ) : (
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            {items.map((e) => (
              <Card key={e.id} size="small" styles={{ body: { padding: 16 } }}>
                {/* 标题行：类型 + 标题 */}
                <div style={{ marginBottom: 8 }}>
                  <Space size={8} wrap>
                    {typeTag(e.eventType)}
                    <Text strong style={{ fontSize: 15 }}>{e.title}</Text>
                  </Space>
                </div>

                {/* 元信息行：时间 / 来源 / 原文 / 情绪 / 重要性 / 可信度 */}
                <Space size={[8, 4]} wrap style={{ marginBottom: e.content ? 8 : 0, color: 'rgba(0,0,0,0.45)' }}>
                  <Text type="secondary"><ClockCircleOutlined /> {fmtTime(e.eventTime)}</Text>
                  {e.source && <Text type="secondary">· {e.source}</Text>}
                  {e.url && (
                    <Link href={e.url} target="_blank" rel="noreferrer">
                      <LinkOutlined /> 原文
                    </Link>
                  )}
                  {sentimentTag(e.sentiment, e.sentimentScore)}
                  {e.importance != null && <Tag color="orange">重要 {e.importance}</Tag>}
                  {e.credibility != null && <Tag color="cyan">可信 {e.credibility}</Tag>}
                </Space>

                {/* 正文（可展开） */}
                {e.content && (
                  <Paragraph
                    type="secondary"
                    ellipsis={{ rows: 2, expandable: true, symbol: '展开' }}
                    style={{ marginBottom: 8 }}
                  >
                    {e.content}
                  </Paragraph>
                )}

                {/* 关联个股 */}
                {e.relatedStocks.length > 0 && (
                  <div style={{ marginBottom: 4 }}>
                    <Text type="secondary" style={{ marginRight: 8 }}>
                      <StockOutlined /> 关联个股：
                    </Text>
                    {e.relatedStocks.map((s, i) => (
                      <Tag key={`${s.code ?? s.name}-${i}`} color="blue" style={{ marginBottom: 4 }}>
                        {s.name ?? s.code}{s.code && s.name ? ` ${s.code}` : ''}
                      </Tag>
                    ))}
                  </div>
                )}

                {/* 关联概念 */}
                {e.relatedConcepts.length > 0 && (
                  <div>
                    <Text type="secondary" style={{ marginRight: 8 }}>
                      <TagsOutlined /> 关联概念：
                    </Text>
                    {e.relatedConcepts.map((c, i) => (
                      <Tag key={`${c}-${i}`} style={{ marginBottom: 4 }}>{c}</Tag>
                    ))}
                  </div>
                )}
              </Card>
            ))}
          </Space>
        )}

        {total > 0 && (
          <div style={{ marginTop: 16, textAlign: 'right' }}>
            <Pagination
              current={page}
              pageSize={PAGE_SIZE}
              total={total}
              showSizeChanger={false}
              showTotal={(t) => `共 ${t} 条`}
              onChange={setPage}
            />
          </div>
        )}
      </Spin>
    </div>
  );
};

export default Intelligence;
