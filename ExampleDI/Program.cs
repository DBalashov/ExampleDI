using System;
using System.Linq;
using System.Threading;
using DryIoc;

namespace ExampleDI
{
    internal class Program
    {
        static void Main()
        {
            //exampleSimple();
            //exampleScopes();
            //exampleThreads();
            
            //exampleWrapperFunctors();
            //exampleDisposable();
            //exampleDisposable2();
            
            //exampleWithKeys();
            //exampleFieldsPropertiesInjections();
            //exampleScopeLifetime();
            
            //exampleScopeRegister();
            //exampleLateRegistration();
            exampleDecorator();
        }

        static void exampleSimple() // простой пример регистрации в контейнере класса Locale и зависимого от него Parameter
        {
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("RU"));
            c.RegisterDelegate<IParameter>(r => new ParameterImpl(r, "Motion"));

            // никакого контекста, никакого трекинга - каждый вызов Resolve<T> создает новый экземпляр согласно регистрации
            Console.WriteLine(c.Resolve<ILocale>()); // RU#0
            Console.WriteLine(c.Resolve<ILocale>()); // RU#1
            Console.WriteLine(c.Resolve<ILocale>()); // RU#2

            // никакого контекста, никакого трекинга - каждый вызов Resolve<T> создает новый экземпляр согласно регистрации
            // в ParameterImpl используется вложенный ресолв интерфейса ILocale из контейнера - и каждый такой ресолв будет создавать новый экземпляр ILocale (#3, #4, #5) 
            Console.WriteLine(c.Resolve<IParameter>()); // Motion#0 with locale: RU#3
            Console.WriteLine(c.Resolve<IParameter>()); // Motion#1 with locale: RU#4
            Console.WriteLine(c.Resolve<IParameter>()); // Motion#2 with locale: RU#5
        }
        
        static void exampleThreads() // пример регистрации класса и его ресолвинга в контексте потока (или другого scope)
        {
            // в каждом scope класс ресолвица в один экземпляр и живёт в scope в его области видимости  
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("RU"), Reuse.Scoped);

            CountdownEvent evStopped = new CountdownEvent(4);
            for (var i = 0; i < evStopped.InitialCount; i++)
            {
                var threadName = "Thread/" + i;
                new Thread(() =>
                {
                    using (var scope = c.OpenScope())
                    {
                        Console.WriteLine("{0}:\t{1}", threadName, scope.Resolve<ILocale>()); // create 'thread local' instance
                        Console.WriteLine("{0}:\t{1}", threadName, scope.Resolve<ILocale>()); // created 'thread local' instance
                        Console.WriteLine("{0}:\t{1}", threadName, scope.Resolve<ILocale>()); // created 'thread local' instance
                    }

                    evStopped.Signal();
                }).Start();
            }

            evStopped.Wait();
        }

        static void exampleWrapperFunctors() // использование функторов и их вызова для передачи параметров в конструкторы реализаций  
        {
            // при необходимости lazy-создания экземпляра класса можно ресолвить функторы с теми же параметрами, какие есть у конструктора класса
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.Register<ILocale, LocaleImpl>(Reuse.Transient); // transient - не сохранять ссылок на экземпляры класса в scope 

            Console.WriteLine(c.Resolve<Func<string, ILocale>>()("RU")); // get functor & create ILocale with parameter RU
            Console.WriteLine(c.Resolve<Func<string, ILocale>>()("EN")); // get functor & create ILocale with parameter EN
            
            using (var scope = c.OpenScope())
            {
                var f = scope.Resolve<Func<string, ILocale>>();
                Console.WriteLine(f("RU")); // get functor & create ILocale with parameter RU
                Console.WriteLine(f("EN")); // get functor & create ILocale with parameter EN
            }
            
            Console.WriteLine("----------");
            c = new Container();
            c.Register<ILocale, LocaleImpl>(Reuse.Scoped); // scoped - сохранять ссылки на созданные экземпляры классов
            using (var scope = c.OpenScope())
            {
                var f = scope.Resolve<Func<string, ILocale>>();
                Console.WriteLine(f("RU")); // get functor & create ILocale with parameter RU
                Console.WriteLine(f("EN")); // get functor & create ILocale with parameter EN ->
                                            // (!) вернется предыдущий экземпляр класса созданный с параметрам RU - потому что это происходит в одном и том же scope
                                            // и результат функтора сохраняется в scope как результат ресолвинга
                                            // чтобы этого не происходило и результат функтора не трекался в scope - надо при создании контейнера/scope в rules указать rules.WithIgnoringReuseForFuncWithArgs)  
            }
        }

        static void exampleScopes() // пример с созданием разных scope, созданием объектов и областью жизни объектов
        {
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());

            c.Register<ILocale>(
                made: Made.Of(() => new LocaleImpl(Arg.Of<string>(IfUnresolved.ReturnDefault))),
                reuse: Reuse.Scoped); // с трекингом в scope 

            c.Register<IParameter, ParameterImpl>(
                made: Made.Of(FactoryMethod.ConstructorWithResolvableArguments),
                reuse: Reuse.Transient); // не трекать - каждый resolve => новый экземпляр

            using (IResolverContext scope = c.OpenScope())
            {
                var l = scope.Resolve<ILocale>(new[] {"RU"}); // первый вызов -> создание экземпляра -> регистрация в scope. Этот вызов для примера, в реальном коде его делать нет необходимости,
                                                              // потому что создание экземпляра и регистрация в scope будет выполнена при первом вызове Resolve<T> 
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Motion")); // RU#0
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Move"));   // RU#0 - в обоих случаях это один и тот же экземпляр.
            }
            // первый scope кончился и все ссылки (что было там натрекано при создании классов) также удалены
            
            using (IResolverContext scope = c.OpenScope())
            {
                var l = scope.Resolve<ILocale>(new[] {"EN"}); // снова первый вызов уже в другом scope -> создание экземпляра -> регистрация в scope
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Motion"));
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Move"));
            }
            
            // пример с перерегистраций класса и фасадом к контейнеру.
            // В некоторых случаях необходимо перекрыть регистрацию класса (например другой реализацией интерфейса).
            // Чтобы не повлиять на оригинальные регистрации контейнера и перекрытие регистрации работало только в контексте вызова - используется создание фасада,
            // который обеспечивает работу со всеми правилами создания классов в оригинальном контейнере, но замещает реализации интерфейса на те новые регистрации, которые в нем делаются
            // Для остального кода, который использует scope и Resolve<T> - ничего не меняется.
            Console.WriteLine(Environment.NewLine + "--------- With facade & manual registration ---------------------------------------------------------------------");
            IContainer facade = c.CreateFacade();
            facade.RegisterDelegate<ILocale>(r => new LocaleImpl("RU"), ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            using (var scope = facade.OpenScope())
            {
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Motion"));
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Move"));
            }

            facade = c.CreateFacade();
            facade.RegisterDelegate<ILocale>(r => new LocaleImpl("EN"), ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            using (var scope = facade.OpenScope())
            {
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Motion"));
                Console.WriteLine(scope.Resolve<Func<string, IParameter>>()("Move"));
            }
        }
        
        static void exampleDisposable() // пример с видимости scope и области жизни объектов - с transient регистрацией 
        {
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("RU"), Reuse.Transient);
            c.Register<IParameterDisposable, ParameterImplDisposable>(
                made: Made.Of(FactoryMethod.ConstructorWithResolvableArguments),
                setup: Setup.With(allowDisposableTransient: true),
                reuse: Reuse.Transient);

            using (var scope = c.OpenScope())
            {
                Console.WriteLine("OUTER SCOPE:");
                using (var item1 = scope.Resolve<Func<string, IParameterDisposable>>()("Motion"))
                {
                    Console.WriteLine(item1); // RU#0
                    using (var item2 = scope.Resolve<Func<string, IParameterDisposable>>()("Move"))
                    {
                        Console.WriteLine(" " + item2); // RU#1
                        Console.WriteLine("----- dispose after this line:");
                        // обратить внимание (!): Move#1 with locale: RU#2 - Disposed
                        // #2 потому что для вывода текста в Dispose используется второй вызов Resolve, который ресолвит ILocale во второе создание класса, т.к. нет трекинга в scope   
                    }
                    Console.WriteLine("----- IParameterDisposable[Motion] dispose after this line:");
                    // то же самое и тут - RU#3 (а не RU#0 как это было при создании)
                }

                using (var innerScope = scope.OpenScope())
                {
                    Console.WriteLine(Environment.NewLine + "  INNER SCOPE:");
                    using (var item1 = innerScope.Resolve<Func<string, IParameterDisposable>>()("Motion"))
                    {
                        Console.WriteLine("  " + item1);
                        using (var item2 = innerScope.Resolve<Func<string, IParameterDisposable>>()("Move"))
                            Console.WriteLine("  " + item2);
                    }
                }
            }
        }
        
        static void exampleDisposable2() // пример с видимости scope и области жизни объектов - с scoped регистрацией
        {
            // в отличие от примера exampleDisposable - здесь регистрация ILocale - scoped
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.RegisterDelegate<ILocale>(r => new LocaleDisposable("RU"), Reuse.Scoped);
            c.Register<IParameterDisposable, ParameterImplDisposable>(
                made: Made.Of(FactoryMethod.ConstructorWithResolvableArguments),
                setup: Setup.With(allowDisposableTransient: true),
                reuse: Reuse.Transient);
            
            using (var scope = c.OpenScope())
            {
                Console.WriteLine("OUTER SCOPE:");
                using (var item1 = scope.Resolve<Func<string, IParameterDisposable>>()("Motion"))
                {
                    Console.WriteLine(item1); // RU#0
                    using (var item2 = scope.Resolve<Func<string, IParameterDisposable>>()("Move"))
                    {
                        Console.WriteLine(item2); // RU#0
                        // dispose тоже будет с RU#0
                    }
                    // и тут тоже dispose будет с RU#0
                }
                
                // а тут открывается новый scope, в нём нет экземпляра RU, поэтому при первом Resolve будет создан второй экземпляр ILocale с RU#1
                // и далее он будет использован в обоих IParameterDisposable 
                using (var innerScope = scope.OpenScope())
                {
                    Console.WriteLine(Environment.NewLine + "INNER SCOPE:");
                    using (var item1 = innerScope.Resolve<Func<string, IParameterDisposable>>()("Motion"))
                    {
                        Console.WriteLine(item1);
                        using (var item2 = innerScope.Resolve<Func<string, IParameterDisposable>>()("Move"))
                            Console.WriteLine(item2);
                    }
                    
                    // здесь будет dispose RU#1 т.к. innerScope кончается
                }
                
                // а здесь будет dispose RU#0 - т.к. scope кончается 
            }
        }

        static void exampleWithKeys() // пример регистрации разных реализаций одного и того же интерфейса, но с разными ключами, по которым их потом можно запросить по отдельности 
        {
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            
            // для простоты они все Singleton
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("Russian"), Reuse.Singleton); // если не зарегать дефолтную (serviceKey==null) реализацию то вызов (*) приведёт к исключению
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("English"), Reuse.Singleton, serviceKey: "EN");
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("Italiano"), Reuse.Singleton, serviceKey: "IT");

            Console.WriteLine("ILocale implementations registered: " + c.ResolveMany<ILocale>().Count());
            Console.WriteLine(c.Resolve<ILocale>()); // (*) create 
            Console.WriteLine(c.Resolve<ILocale>()); // (*) already created, return instance
            Console.WriteLine(c.Resolve<ILocale>("IT")); // create
            Console.WriteLine(c.Resolve<ILocale>("EN")); // create
            
            Console.WriteLine(c.Resolve<ILocale>()); // return with default service key = null
        }
        
        static void exampleFieldsPropertiesInjections() // пример с инъекцией полей и автоматическим ресолвингом интерфейсов
        {
            var c = new Container(rules => rules.With(propertiesAndFields: PropertiesAndFields.All(ifUnresolved: IfUnresolved.Throw)), // или IfUnresolved.ReturnDefault => NULL
                                  new AsyncExecutionFlowScopeContext());
            
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("RU"), Reuse.Singleton);
            c.Register<IInjections, Injections>(Reuse.Transient);
 
            // Можно зарегистрировать инициализатор объекта, который вызывается при создании каждого экземпляра нового объекта, реализующего указанный интерфейс.
            // В инициализаторе также можно сделать с объектом какие-то вещи (логирование, ... )
            c.RegisterInitializer<ILocale>((item, r) =>
                                           {
                                               if (item.Locale.StartsWith("RU")) item.Locale = "FR";
                                           },
                                           r => { return false; }); // опциональный функтор (в него передается Request на создание объекта), посмотрев который можно вернуть true/false -
                                                                    // если вернуть false, то инициализатор объекта (первый параметр RegisterInitializer<T>) вызываться не будет 

            Console.WriteLine(c.Resolve<ILocale>()); // нет необходимости делать это в реальном коде, т.к. объекты создаются автоматически,
            Console.WriteLine(c.Resolve<ILocale>()); // это только для демонстрации того, что при инъекции в поле попадает экземпляр того же класса
            
            Console.WriteLine(c.Resolve<IInjections>()); // при ресолвинге происходит инъекция всех известных контейнеру интерфейсов согласно scope и правилам ресолвинга,
                                                         // если класса ещё не создано - он создается и регистрируется в контейнере. Код полностью аналогичен ресолвингу IInjections и ручному присвоению поля Locale = c.Resolve<ILocale>()
        }

        static void exampleScopeLifetime() // пример областей жизни Scope
        {
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.RegisterDelegate<ILocaleDisposable>(r => new LocaleDisposable("RU"), Reuse.Scoped);
            
// При вызове scope.OpenScope будет ошибка "Container is disposed and should not be used" - родительский scope уже уничтожен к моменту создания дочернего scope
// так делать не надо:
            using (var scope = c.OpenScope())
            {
                Console.WriteLine(scope.Resolve<ILocale>());
                new Thread(() =>
                {
                    Thread.Sleep(500); // подождем пока родительский scope уничтожится
                    using (var threadScope = scope.OpenScope()) // здесь будет исключение
                    {
                        Console.WriteLine(threadScope.Resolve<ILocale>());
                    }
                }).Start();
                Thread.Sleep(100);
            }
// варианты использования:
// 1) в родительском using scope надо дождаться окончания выполняемого потока (потоков) и потом выходить за область видимости родительского scope
// 2) создавать дочерний scope пока родительский ещё живой - тогда родительский scope не уничтожится, пока жив хотя бы один дочерний scope.
//    для выполнения этого примера в вышеприведенном коде Thread.Sleep(500) убрать - тогда за счет Thread.Sleep(100) дочерний scope успеет создастся до уничтожения родительского scope
//    в данном случае это гарантированно будет работать, однако в реальном коде - там где поток старует после закрытия области родительского scope - будет исключение
        }
        
        static void exampleScopeRegister() // пример создания фасада и дополнительных регистраций интерфейсов в нём, в дополнение к уже существующим в родительском контейнере
        {
            var c = new Container(rules => rules, new AsyncExecutionFlowScopeContext());
            c.RegisterDelegate<ILocale>(r => new LocaleImpl("RU"), Reuse.Scoped);

            using (var scope = c.OpenScope("R1"))
            {
                var parmBefore = scope.Resolve<IParameter>(IfUnresolved.ReturnDefault);
                Console.WriteLine("R1<IParameter> (before facade): {0}", parmBefore?.ToString() ?? "NULL");

                using (var facade = c.WithRegistrationsCopy().WithoutSingletonsAndCache()) // фасад является таким же контейнером как и родительский, содержит копии регистраций интерфейсов
                                                                                           // также может принять для регистрации другие абстракции, которые будут жить пока живёт фасад
                {
                    var localeInFacade = facade.Resolve<ILocale>();
                    Console.WriteLine("R1<ILocale>    (inside facade): {0}", localeInFacade?.ToString() ?? "NULL");    
                    facade.RegisterDelegate<IParameter>(r => new ParameterImpl(r, "Motion"), Reuse.Scoped);
                    using (var scopeInner = facade.OpenScope("R2"))
                    {
                        var r2 = scopeInner.Resolve<IParameter>(IfUnresolved.ReturnDefault);
                        Console.WriteLine("R1<IParameter> (inside facade): {0}", r2);
                    }
                }

                var parmAfter = scope.Resolve<IParameter>(IfUnresolved.ReturnDefault);
                Console.WriteLine("R1<IParameter>  (after facade): {0}", parmAfter?.ToString() ?? "NULL");
            }
        }
        
        static void exampleLateRegistration() // поздняя регистрация позволяет зарегистрировать зависимые объекты уже после создания основных объектов и обеспечить правильный ресолвинг абстракций
        {
            var c = new Container(rules => rules.With(propertiesAndFields: PropertiesAndFields.All(ifUnresolved: IfUnresolved.Throw)), new AsyncExecutionFlowScopeContext());
            c.RegisterPlaceholder<ILocale>(); // затычка на ILocale и фабрику Func<ILocale> для поздней регистрации интерфейса в рамках Scope 
            c.Register<IInjections, InjectionPlaceholder>(Reuse.Scoped);
            using (var scope = c.OpenScope("R1"))
            {
                var parm = scope.Resolve<IInjections>(); // в поле locale экземпляра InjectionLazy будет помещена фабрика Func<ILocale>, которая является заглушкой на ресолвинг ILocale, который будет заполнен позже
                
                // Console.WriteLine("Late registration: " + parm); // тут будет exception, потому что Func<ILocale> не найдет никакой реализации интерфейса в scope  

                scope.UseInstance<ILocale>(new LocaleImpl("RU")); // поздняя регистрация в рамках области жизни Scope, уже после ресолвинга IInjections
                Console.WriteLine("Late registration: " + parm); // а вот тут фабрика уже найдет правильную реализацию ILocale в Scope
            }
        }

        static void exampleDecorator()
        {
            var c = new Container(rules => rules.With(propertiesAndFields: PropertiesAndFields.All(ifUnresolved: IfUnresolved.Throw)), new AsyncExecutionFlowScopeContext());
            c.Register<IHandler, HandlerImpl>(); // обычная регистрация - при вызове Handler будет вызван соответствующий метод и выведено Handler called
            
            // c.Register<IHandler, HandlerDecorator>(setup: Setup.Decorator);
            // ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^ если раскомментарить эту строку - произойдет регистрация декоратора к IHandler 
            // везде, где происходит ресолв IHandler (кроме самого декоратора) - ресолв будет в декоратора, который в поле IHandler получит реального HandlerImpl
            // (и/или другие зависимости, которые можно задать как параметры конструктора - например (IHandler handler, ILogger logger) - и которые доступны к моменту ресолва декоратора.
            // таким образом, одной регистрацией декоратора можно одномоментно и везде вставить прослойку перед реальной реализацией
            // и (например) внутри декоратора измерить скорость вызовов методов и/или журналировать вызовы в отладочной версии 
            c.Resolve<IHandler>().Handle();
        } 
    }

    #region ILocale
    // ==========================
    interface ILocale
    {
        string Locale { get; set; }
    }

    public class LocaleImpl : ILocale
    {
        static int counter;
        public string Locale { get; set; }

        public LocaleImpl(string locale) => Locale = locale + "#" + (counter++);

        public override string ToString() => Locale;
    }
    
    interface ILocaleDisposable : ILocale, IDisposable { }
    public class LocaleDisposable : LocaleImpl, ILocaleDisposable
    {
        public LocaleDisposable(string locale) : base(locale) { }
        public void Dispose() => Console.WriteLine(this + " => Disposed");
    }
    #endregion

    #region IParameter (depends from ILocale)
    // ==========================
    interface IParameter
    {
        string Name { get; }
        string FullName { get; }
    }
    
    public class ParameterImpl:IParameter
    {
        static int counter;
        readonly IResolver r;

        public string Name { get; }
        public string FullName => $"{Name} with locale: " + r.Resolve<ILocale>().Locale;

        public ParameterImpl(IResolver resolver, string name)
        {
            r = resolver;
            Name = name + "#" + (counter++);
        }

        public override string ToString() => FullName;
    }

    interface IParameterDisposable : IParameter, IDisposable { }
    public class ParameterImplDisposable : ParameterImpl, IParameterDisposable
    {
        public ParameterImplDisposable(IResolver resolver, string name) : base(resolver, name) { }
        public void Dispose() => Console.WriteLine(this + " => Disposed");
    }
    #endregion
    
    #region class with automatic injections
    interface IInjections { }
    
    class Injections : IInjections
    {
        public ILocale locale;

        public override string ToString() => "Injections with: " + locale;
    }
    
    class InjectionPlaceholder : IInjections
    {
        public Func<ILocale> locale;

        public override string ToString() => "Injections with: " + locale();
    }
    
    class InjectionLazy : IInjections
    {
        public Lazy<ILocale> locale;

        public override string ToString() => "Injections with: " + locale.Value;
    }
    #endregion

    #region decorator example
    interface IHandler
    {
        void Handle();
    }
    
    class HandlerImpl : IHandler 
    {
        public void Handle() 
        { 
            Console.WriteLine("Handler called"); 
        }
    }

    class HandlerDecorator : IHandler
    {
        public IHandler handler;

        public void Handle() 
        { 
            Console.WriteLine("Before");
            handler.Handle();
            Console.WriteLine("After");
        }
    }
    #endregion
}