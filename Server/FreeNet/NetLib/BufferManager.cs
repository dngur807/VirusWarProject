using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

/**
 * 송 수신 버퍼 풀링 기법
 * BufferManager
 * 다음 으로 버퍼 관리에 대한 구현을 보겠습니다.
 * SocketAsyncEventArgs마다 버퍼가 하나씩 필요하다고 설명 드렸습니다.
 * 버퍼라는 것은 바이트 배열로 이루어진 메모리 덩어리 입니다.
 * 
 * BufferManager buffer_manager;  
 * this.buffer_manager = new BufferManager(this.max_connections * this.buffer_size * this.pre_alloc_count, this.buffer_size);
 * 버퍼 전체 크기는 아래 공식으로 계산됩니다.
 * 버퍼의 전체 크기 = 최대 동접 수치 * 버퍼 하나의 크기 * (전송용 , 수신용)
 * 
 * 전송용 한개, 수신용 한개 총 두개가 필요하기 때문에 pre_alloc_count = 2로 했습니다.

*/
namespace NetLib
{
    internal class BufferManager
    {
        int m_numBytes;
        byte[] m_buffer;
        Stack<int> m_freeIndexPool;
        int m_currentIndex;
        int m_bufferSize;

        public BufferManager(int totalBytes, int bufferSize)
        {
            m_numBytes = totalBytes;
            m_currentIndex = 0;
            m_bufferSize = bufferSize;
            m_freeIndexPool = new Stack<int>();
        }


        public void InitBuffer()
        {
            m_buffer = new byte[m_numBytes];

        }

        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (m_freeIndexPool.Count > 0)
            {
                args.SetBuffer(m_buffer, m_freeIndexPool.Pop(), m_bufferSize);
            }
            else
            {
                if ((m_numBytes - m_bufferSize) < m_currentIndex)
                {
                    return false;
                }

                args.SetBuffer(m_buffer, m_currentIndex, m_bufferSize);
                m_currentIndex += m_bufferSize;
            }
            return true;
        }

        public void FreeBuffer(SocketAsyncEventArgs args)
        {
            m_freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }
    }
}
