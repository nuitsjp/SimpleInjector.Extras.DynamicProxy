﻿using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Castle.DynamicProxy;
using SimpleInjector;

namespace RedSheeps.SimpleInjector.DynamicProxy
{
    public static class InterceptorExtensions
    {
        public static void Intercept<TInterface>(this Container container, params Type[] interceptors)
        {
            var interceptWith =
                new InterceptionHelper(
                    type => type == typeof(TInterface) 
                        || type.GetTypeInfo().ImplementedInterfaces.Any(x => x == typeof(TInterface)), 
                    e => BuildInterceptorExpressions(container, interceptors), 
                    type => typeof(TInterface));

            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith<TInterceptor>(
            this Container container, Predicate<Type> predicate)
            where TInterceptor : class, IInterceptor
        {
            container.Options.ConstructorResolutionBehavior.GetConstructor(typeof(TInterceptor));

            var interceptWith =
                new InterceptionHelper(predicate, e => BuildInterceptorExpressions(container, typeof(TInterceptor)));

            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith(
            this Container container, Predicate<Type> predicate, params IInterceptor[] interceptors)
        {
            var interceptWith =
                new InterceptionHelper(predicate, e => Expression.Constant(interceptors));

            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith(
            this Container container, Predicate<Type> predicate, params Type[] interceptors)
        {
            foreach (var interceptor in interceptors)
            {
                if (interceptor.GetTypeInfo().ImplementedInterfaces.All(interfaceType => interfaceType != typeof(IInterceptor)))
                    throw new ArgumentException($"{interceptor} is not implemant {typeof(IInterceptor)}");
            }

            var interceptWith =
                new InterceptionHelper(predicate, e => BuildInterceptorExpressions(container, interceptors));

            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith(
            this Container container, Predicate<Type> predicate, Func<IInterceptor> interceptorCreator)
        {
            var interceptWith = 
                new InterceptionHelper(
                    predicate, 
                    e => Expression.NewArrayInit(
                        typeof(IInterceptor), 
                        Expression.Invoke(Expression.Constant(interceptorCreator))));
            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith(
            this Container container, Predicate<Type> predicate, Func<IInterceptor[]> interceptorsCreator)
        {
            var interceptWith =
                new InterceptionHelper(predicate, e => Expression.Invoke(Expression.Constant(interceptorsCreator)));
            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith(
            this Container container, Predicate<Type> predicate, Func<ExpressionBuiltEventArgs, IInterceptor> interceptorCreator)
        {
            var interceptWith =
                new InterceptionHelper(
                    predicate,
                    e => Expression.NewArrayInit(
                        typeof(IInterceptor),
                        Expression.Invoke(
                            Expression.Constant(interceptorCreator),
                            Expression.Constant(e))));

            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        public static void InterceptWith(
            this Container container, Predicate<Type> predicate, Func<ExpressionBuiltEventArgs, IInterceptor[]> interceptorsCreator)
        {
            var interceptWith =
                new InterceptionHelper(
                    predicate,
                    e => Expression.Invoke(
                        Expression.Constant(interceptorsCreator),
                        Expression.Constant(e)));

            container.ExpressionBuilt += interceptWith.OnExpressionBuilt;
        }

        private static Expression BuildInterceptorExpression(Container container, Type interceptorType)
        {
            var interceptorRegistration = container.GetRegistration(interceptorType);

            if (interceptorRegistration == null)
            {
                // This will throw an ActivationException
                container.GetInstance(interceptorType);
            }

            return interceptorRegistration.BuildExpression();
        }

        private static Expression BuildInterceptorExpressions(Container container, params Type[] interceptorTypes)
        {
            return Expression.NewArrayInit(
                typeof(IInterceptor),
                interceptorTypes.Select(x => BuildInterceptorExpression(container, x)).ToArray());
        }

        private class InterceptionHelper
        {
            private static readonly ProxyGenerator Generator = new ProxyGenerator();

            private static readonly Func<Type, object, IInterceptor[], object> CreateClassProxyWithTarget =
                (p, t, i) => Generator.CreateClassProxyWithTarget(p, t, i);

            private static readonly Func<Type, object, IInterceptor[], object> CreateInterfaceProxyWithTarget =
                (p, t, i) => Generator.CreateInterfaceProxyWithTarget(p, t, i);

            private readonly Predicate<Type> _predicate;
            private readonly Func<ExpressionBuiltEventArgs, Expression> _buildInterceptorExpression;
            private readonly Func<Type, Type> _getProxyType;

            public InterceptionHelper(Predicate<Type> predicate, Func<ExpressionBuiltEventArgs, Expression> buildInterceptorExpression)
                : this(predicate, buildInterceptorExpression, type => type)
            {
            }

            public InterceptionHelper(
                Predicate<Type> predicate, 
                Func<ExpressionBuiltEventArgs, Expression> buildInterceptorExpression,
                Func<Type, Type> getProxyType)
            {
                _predicate = predicate;
                _buildInterceptorExpression = buildInterceptorExpression;
                _getProxyType = getProxyType;
            }

            public void OnExpressionBuilt(object sender, ExpressionBuiltEventArgs e)
            {
                if (_predicate(e.RegisteredServiceType))
                {
                    e.Expression = BuildProxyExpression(e);
                }
            }

            private Expression BuildProxyExpression(ExpressionBuiltEventArgs e)
            {
                var expr = _buildInterceptorExpression(e);

                var proxyType = _getProxyType(e.RegisteredServiceType);
                var createProxy =
                    proxyType.GetTypeInfo().IsInterface ?
                    CreateInterfaceProxyWithTarget :
                    CreateClassProxyWithTarget;

                return Expression.Convert(
                    Expression.Invoke(Expression.Constant(createProxy),
                        Expression.Constant(proxyType, typeof(Type)),
                        e.Expression,
                        expr),
                    proxyType);
            }
        }

    }
}
