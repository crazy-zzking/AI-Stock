import React, { useState, useRef, useEffect } from 'react';
import { Card, Input, Button, Select, Space, Typography, Spin } from 'antd';
import { SendOutlined, RobotOutlined, UserOutlined, ClearOutlined } from '@ant-design/icons';
import { getLLMModels, sendChat } from '../api';
import type { LLMModel } from '../types/models';

const { TextArea } = Input;
const { Text, Paragraph } = Typography;

interface ChatMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: number;
  modelName?: string;
  responseTime?: number;
}

/** AI 对话面板 — 与 LLM 进行交互对话 */
const AIChat: React.FC = () => {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState('');
  const [systemPrompt, setSystemPrompt] = useState('');
  const [models, setModels] = useState<LLMModel[]>([]);
  const [selectedModel, setSelectedModel] = useState<string | undefined>(undefined);
  const [sending, setSending] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    loadModels();
  }, []);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const loadModels = async () => {
    try {
      const res = await getLLMModels();
      const enabled = (res.data || []).filter((m) => m.isEnabled);
      setModels(enabled);
    } catch {
      // ignore
    }
  };

  const handleSend = async () => {
    const text = input.trim();
    if (!text) return;

    const userMsg: ChatMessage = { role: 'user', content: text, timestamp: Date.now() };
    setMessages((prev) => [...prev, userMsg]);
    setInput('');
    setSending(true);

    try {
      const startTime = Date.now();
      const res = await sendChat(text, systemPrompt || undefined, selectedModel);
      const data = res.data;

      const assistantMsg: ChatMessage = {
        role: 'assistant',
        content: data.content || data.errorMessage || '(空响应)',
        timestamp: Date.now(),
        modelName: data.modelName || selectedModel,
        responseTime: data.responseTimeMs || (Date.now() - startTime),
      };
      setMessages((prev) => [...prev, assistantMsg]);
    } catch (err: any) {
      const errorMsg: ChatMessage = {
        role: 'system',
        content: `错误: ${err?.response?.data?.error || err?.message || '请求失败'}`,
        timestamp: Date.now(),
      };
      setMessages((prev) => [...prev, errorMsg]);
    } finally {
      setSending(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleClear = () => setMessages([]);

  return (
    <div>
      <h2>
        <RobotOutlined /> AI 对话
        <Button
          icon={<ClearOutlined />}
          size="small"
          style={{ marginLeft: 12 }}
          onClick={handleClear}
          disabled={messages.length === 0}
        >
          清空对话
        </Button>
      </h2>

      <Card style={{ marginBottom: 16 }}>
        <Space direction="vertical" style={{ width: '100%' }} size="small">
          <Space>
            <span style={{ fontWeight: 500, fontSize: 14 }}>System Prompt:</span>
          </Space>
          <TextArea
            value={systemPrompt}
            onChange={(e) => setSystemPrompt(e.target.value)}
            placeholder="可选：设置系统角色提示词，如「你是一个专业的股票分析师...」"
            autoSize={{ minRows: 1, maxRows: 3 }}
            style={{ maxWidth: 600 }}
          />
          <Space wrap>
            <span style={{ fontWeight: 500 }}>模型:</span>
            <Select
              value={selectedModel}
              onChange={setSelectedModel}
              allowClear
              placeholder="自动选择"
              style={{ minWidth: 180 }}
              options={models.map((m) => ({ value: m.id, label: `${m.name} (${m.model})` }))}
            />
          </Space>
        </Space>
      </Card>

      {/* 对话区域 */}
      <Card
        style={{
          marginBottom: 16,
          minHeight: 400,
          maxHeight: 500,
          overflow: 'auto',
          background: '#fafafa',
        }}
        bodyStyle={{ padding: 16 }}
      >
        {messages.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 60, color: '#bbb' }}>
            <RobotOutlined style={{ fontSize: 48, marginBottom: 16 }} />
            <div>输入消息开始与 AI 对话</div>
          </div>
        ) : (
          messages.map((msg, i) => (
            <div
              key={i}
              style={{
                marginBottom: 16,
                display: 'flex',
                flexDirection: msg.role === 'user' ? 'row-reverse' : 'row',
                alignItems: 'flex-start',
                gap: 8,
              }}
            >
              <div
                style={{
                  width: 32,
                  height: 32,
                  borderRadius: '50%',
                  background: msg.role === 'user' ? '#1677ff' : msg.role === 'system' ? '#ff4d4f' : '#52c41a',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: '#fff',
                  fontSize: 14,
                  flexShrink: 0,
                }}
              >
                {msg.role === 'user' ? <UserOutlined /> : msg.role === 'system' ? '!' : <RobotOutlined />}
              </div>
              <div
                style={{
                  maxWidth: '75%',
                  background: msg.role === 'user' ? '#1677ff' : msg.role === 'system' ? '#fff2f0' : '#fff',
                  color: msg.role === 'user' ? '#fff' : '#333',
                  padding: '10px 14px',
                  borderRadius: 12,
                  border: msg.role === 'assistant' ? '1px solid #e8e8e8' : undefined,
                  wordBreak: 'break-word',
                }}
              >
                <Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{msg.content}</Paragraph>
                {msg.modelName && (
                  <Text type="secondary" style={{ fontSize: 11, display: 'block', marginTop: 4 }}>
                    {msg.modelName} · {msg.responseTime}ms
                  </Text>
                )}
              </div>
            </div>
          ))
        )}
        <div ref={messagesEndRef} />
        {sending && (
          <div style={{ textAlign: 'center', padding: 8 }}>
            <Spin size="small" /> AI 思考中...
          </div>
        )}
      </Card>

      {/* 输入区 */}
      <Card>
        <Space.Compact style={{ width: '100%' }}>
          <TextArea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="输入消息，Enter 发送，Shift+Enter 换行"
            autoSize={{ minRows: 1, maxRows: 4 }}
            disabled={sending}
            style={{ flex: 1 }}
          />
          <Button
            type="primary"
            icon={<SendOutlined />}
            onClick={handleSend}
            loading={sending}
            style={{ height: 'auto' }}
          >
            发送
          </Button>
        </Space.Compact>
      </Card>
    </div>
  );
};

export default AIChat;
