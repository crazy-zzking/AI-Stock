import React, { useState } from 'react';
import { AutoComplete, Input } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { getStockList } from '../api';
import type { StockInfo } from '../types/models';

interface StockSearchProps {
  /** 选择回调 */
  onSelect: (stock: StockInfo) => void;
  /** 占位文本 */
  placeholder?: string;
  /** 宽度 */
  style?: React.CSSProperties;
}

/** 股票搜索选择器 — 支持代码/名称模糊搜索 */
const StockSearch: React.FC<StockSearchProps> = ({
  onSelect,
  placeholder = '输入股票代码或名称搜索...',
  style = { width: 240 },
}) => {
  const [options, setOptions] = useState<{ value: string; label: string; stock: StockInfo }[]>([]);
  const [loading, setLoading] = useState(false);

  const handleSearch = async (keyword: string) => {
    if (!keyword || keyword.length < 1) {
      setOptions([]);
      return;
    }
    setLoading(true);
    try {
      const res = await getStockList();
      const stocks = res.data || [];
      const kw = keyword.toUpperCase();
      const filtered = stocks
        .filter(
          (s) =>
            s.code.includes(kw) ||
            s.name.includes(keyword) ||
            s.name.toUpperCase().includes(kw)
        )
        .slice(0, 20)
        .map((s) => ({
          value: s.code,
          label: `${s.code}  ${s.name}`,
          stock: s,
        }));
      setOptions(filtered);
    } catch {
      setOptions([]);
    } finally {
      setLoading(false);
    }
  };

  const handleSelect = (_value: string, option: { value: string; label: string; stock: StockInfo }) => {
    onSelect(option.stock);
  };

  return (
    <AutoComplete
      options={options}
      onSearch={handleSearch}
      onSelect={handleSelect as (value: string, option: unknown) => void}
      style={style}
      notFoundContent={loading ? '搜索中...' : '无匹配结果'}
    >
      <Input
        placeholder={placeholder}
        prefix={<SearchOutlined />}
        allowClear
      />
    </AutoComplete>
  );
};

export default StockSearch;
