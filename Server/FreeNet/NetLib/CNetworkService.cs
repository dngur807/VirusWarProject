using System;
using System.Net.Sockets;
using System.Threading;

namespace NetLib
{
    public class CNetworkService
    {
        int connected_count;

        // 클라이언트의 접속을 받아들이기 위한 객체입니다.
        CListener client_listener;

        // 메시지 수신 , 전송시 필요한 오브젝트 입니다.
        SocketAsyncEventArgsPool receive_event_args_pool;
        SocketAsyncEventArgsPool send_event_args_pool;

        // 메시지 수신, 전송시 Net 비동기 소켓에서 사용할 버퍼 관리할 객체입니다.
        BufferManager buffer_manager;

        // 클라이언트의 접속이 이루어졌을 때 호출되는 델리게이트 입니다.
        public delegate void SessionHandler(CUserToken token);
        public SessionHandler session_created_callback { get; set; }


        // configs.
        int max_connections;
        int buffer_size;
        readonly int pre_alloc_count = 2;		// read, write

        public CNetworkService()
        {
            this.connected_count = 0;
            this.session_created_callback = null;
        }

        public void Initialize()
        {
            this.max_connections = 10000;
            this.buffer_size = 1024;

            this.receive_event_args_pool = new SocketAsyncEventArgsPool(this.max_connections);
            this.send_event_args_pool = new SocketAsyncEventArgsPool(this.max_connections);

            // preallocate pool of SocketAsyncEventArgs objects
            SocketAsyncEventArgs arg;

            for (int i = 0; i < this.max_connections; i++)
            {
                // 동일한 소켓에 대고 send,receive를 하므로
                // user token은 세션별로 하나씩만 만들어 놓고
                // receive, send EventArgs에서 동일한 token을 참조하도록 구성한다.
                CUserToken token = new CUserToken();

                // receive pool
                {
                    arg = new SocketAsyncEventArgs();
                    arg.Completed += new EventHandler<SocketAsyncEventArgs>(receive_completed);
                    arg.UserToken = token;

                    // 추가된 부분
                    this.buffer_manager.SetBuffer(arg);

                    this.receive_event_args_pool.Push(arg);
                }

                // send Pool
                {
                    arg = new SocketAsyncEventArgs();
                    arg.Completed += new EventHandler<SocketAsyncEventArgs>(send_completed);
                    arg.UserToken = token;

                    // 추가된 부분
                    this.buffer_manager.SetBuffer(arg);

                    // add SocketAsyncEventArg to the pool
                    this.send_event_args_pool.Push(arg);
                }
            }
        }

        public void listen(string host, int port, int backlog)
        {
            this.client_listener = new CListener();
            this.client_listener.callback_on_newclient += on_new_client;
            this.client_listener.start(host, port, backlog);
        }

        /// <summary>
        /// 새로운 클라이언트가 접속 성공 했을 때 호출됩니다.
        /// AcceptAsync의 콜백 매소드에서 호출되며 여러 스레드에서 동시에 호출될 수 있기 때문에 공유자원에 접근할 때는 주의해야 합니다.
        /// </summary>
        /// <param name="client_socket"></param>
        void on_new_client(Socket client_socket, object token)
        {
            Interlocked.Increment(ref this.connected_count);

            Console.WriteLine(string.Format("[{0}] A client connected. handle {1},  count {2}",
                Thread.CurrentThread.ManagedThreadId, client_socket.Handle,
                this.connected_count));

            // 플에서 하나 꺼내와 사용한다.
            SocketAsyncEventArgs receive_args = this.receive_event_args_pool.Pop();
            SocketAsyncEventArgs send_args = this.send_event_args_pool.Pop();

            CUserToken user_token = null;

            // SocketAsyncEventArgs를 생성할 때 만들어 두었던 CUserToken을 꺼내와서
            // 콜백 매소드의 파라미터로 넘겨줍니다.
            if (this.session_created_callback != null)
            {
                user_token = receive_args.UserToken as CUserToken;
                this.session_created_callback(user_token);
            }

            // 이제 클라이언트로 부터 데이터를 수신할 준비를 합니다.
            begin_receive(client_socket, receive_args, send_args);
        }

        void begin_receive(Socket socket, SocketAsyncEventArgs receive_args, SocketAsyncEventArgs send_args)
        {
            // receive_args, send_args 아무곳에서나 꺼내와도 된다. 둘다 동일한 CUserToken을 물고 있다. 
            CUserToken token = receive_args.UserToken as CUserToken;
            token.set_event_args(receive_args, send_args);

            // 생성된 클라이언트 소켓을 보관해 놓고 통신할 때 사용한다.
            token.socket = socket;

            // 데이터를 받을 수 있도록 소켓 매소드를 호출해줍니다.
            // 비동기로 수신할 경우 워커 스레드에서 대기중으로 있다가 Completed에 설정해 놓은 매소드가 호출됩니다.
            // 동기로 완료될 경우에는 직접 완료 매소드를 호출해줘야 한다.
            bool pending = socket.ReceiveAsync(receive_args);

            if (!pending)
            {
                process_receive(receive_args);
            }
        }

        void receive_completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Receive)
            {
                process_receive(e);
                return;
            }
            throw new ArgumentException("The last operation completed on the socket was not a receive.");
        }

        private void process_receive(SocketAsyncEventArgs e)
        {
            CUserToken token = e.UserToken as CUserToken;

            if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
            {
                token.on_receive(e.Buffer, e.Offset, e.BytesTransferred);

                bool pending = token.socket.ReceiveAsync(e);
                if (!pending)
                {
                    process_receive(e);
                }
            }
            else
            {
                Console.WriteLine(string.Format("error {0},  transferred {1}", e.SocketError, e.BytesTransferred));
                close_clientsocket(token);
            }

        }
    }
