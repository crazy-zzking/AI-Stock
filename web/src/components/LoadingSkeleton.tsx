import React from 'react';
import { Skeleton, Card, Row, Col } from 'antd';

interface LoadingSkeletonProps {
  /** 行数 */
  rows?: number;
  /** 是否显示统计卡片 */
  showCards?: boolean;
  /** 统计卡片数量 */
  cardCount?: number;
}

/** 统一加载骨架屏 — 替代 Spin，提供更好的加载体验 */
const LoadingSkeleton: React.FC<LoadingSkeletonProps> = ({
  rows = 3,
  showCards = true,
  cardCount = 4,
}) => {
  return (
    <div style={{ padding: 8 }}>
      {showCards && (
        <Row gutter={16} style={{ marginBottom: 16 }}>
          {Array.from({ length: cardCount }).map((_, i) => (
            <Col span={24 / cardCount} key={i}>
              <Card>
                <Skeleton active paragraph={{ rows: 1 }} title={{ width: '60%' }} />
              </Card>
            </Col>
          ))}
        </Row>
      )}
      <Card>
        <Skeleton active paragraph={{ rows }} />
      </Card>
    </div>
  );
};

export default LoadingSkeleton;
